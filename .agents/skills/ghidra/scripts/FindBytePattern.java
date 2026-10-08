// Finds a byte pattern with wildcards and prints each match as address, RVA, block and
// containing function. Optionally decodes RIP-relative (rel32) operands inside the match and
// ranks the targets, which is how a "global loaded by this code shape" pattern is checked.
//
// Usage (headless):
//   -postScript FindBytePattern.java "48 8B 0D ?? ?? ?? ?? 48 85 C9 74 ??"
//   -postScript FindBytePattern.java "<pattern>" rip:3:7 rip:26:30 same out:C:\re\hits.txt
//
// Arguments:
//   <pattern>        hex bytes separated by spaces; "??" is any byte, "4?" or "?8" is a nibble
//   rip:<disp>:<end> a signed 32-bit displacement at offset <disp> of the match, relative to
//                    the end of its instruction at offset <end>; target = match + end + disp.
//                    Repeat for several operands.
//   same             keep only matches where every rip target is the same address
//   block:<name>     search only this memory block (for example .text); default: every
//                    initialized block
//   max:<n>          print at most n matches (default 200); the counts still cover all
//   out:<file>       also write the report to this file
//
// Quote the pattern and any argument with a space, '=', ',' or ';' in it.
//@category HeroesClientSDK

import java.io.File;
import java.io.PrintWriter;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;
import ghidra.program.model.mem.MemoryBlock;

public class FindBytePattern extends GhidraScript {

	private final List<String> report = new ArrayList<>();

	@Override
	protected void run() throws Exception {
		String[] args = getScriptArgs();
		if (args.length == 0) {
			printerr("Usage: FindBytePattern.java \"<hex pattern with ??>\" [rip:<disp>:<end>]... [same] [block:<name>] [max:<n>] [out:<file>]");
			return;
		}

		String patternText = null;
		List<int[]> rips = new ArrayList<>();
		boolean same = false;
		String blockName = null;
		int max = 200;
		String out = null;
		for (String arg : args) {
			String lower = arg.toLowerCase();
			if (lower.startsWith("rip:")) {
				String[] parts = arg.substring(4).split(":");
				rips.add(new int[] { toInt(parts[0]), toInt(parts[1]) });
			}
			else if (lower.equals("same")) {
				same = true;
			}
			else if (lower.startsWith("block:")) {
				blockName = arg.substring(6);
			}
			else if (lower.startsWith("max:")) {
				max = toInt(arg.substring(4));
			}
			else if (lower.startsWith("out:")) {
				out = arg.substring(4);
			}
			else if (patternText == null) {
				patternText = arg;
			}
			else {
				printerr("Ignoring unknown argument: " + arg);
			}
		}

		byte[][] parsed = parsePattern(patternText);
		byte[] pattern = parsed[0];
		byte[] mask = parsed[1];
		Address imageBase = currentProgram.getImageBase();
		emit("Program " + currentProgram.getName() + ", image base " + imageBase);
		emit("Pattern (" + pattern.length + " bytes): " + patternText);
		for (int[] rip : rips) {
			if (rip[0] < 0 || rip[0] + 4 > pattern.length) {
				printerr("rip displacement offset " + rip[0] + " is outside the pattern");
				return;
			}
		}

		int matches = 0;
		int kept = 0;
		Map<Long, Integer> targets = new LinkedHashMap<>();
		for (MemoryBlock block : currentProgram.getMemory().getBlocks()) {
			if (!block.isInitialized() || (blockName != null && !block.getName().equals(blockName))) {
				continue;
			}
			byte[] bytes = readBlock(block);
			Address start = block.getStart();
			for (int i = 0; i + pattern.length <= bytes.length; i++) {
				if (!matchesAt(bytes, i, pattern, mask)) {
					continue;
				}
				monitor.checkCancelled();
				matches++;
				Address at = start.add(i);
				long[] resolved = new long[rips.size()];
				boolean allSame = true;
				for (int r = 0; r < rips.size(); r++) {
					int disp = readInt32(bytes, i + rips.get(r)[0]);
					resolved[r] = at.getOffset() + rips.get(r)[1] + disp;
					if (r > 0 && resolved[r] != resolved[0]) {
						allSame = false;
					}
				}
				if (same && !allSame) {
					continue;
				}
				kept++;
				if (resolved.length > 0) {
					targets.merge(resolved[0], 1, Integer::sum);
				}
				if (kept <= max) {
					StringBuilder line = new StringBuilder();
					line.append(at).append("  rva 0x").append(Long.toHexString(at.subtract(imageBase)))
							.append("  ").append(block.getName());
					Function f = getFunctionContaining(at);
					if (f != null) {
						line.append("  in ").append(f.getName()).append(" (rva 0x")
								.append(Long.toHexString(f.getEntryPoint().subtract(imageBase))).append(")");
					}
					for (int r = 0; r < resolved.length; r++) {
						line.append("  rip").append(r).append(" -> rva 0x")
								.append(Long.toHexString(resolved[r] - imageBase.getOffset()));
					}
					emit(line.toString());
				}
			}
		}

		emit("Matches: " + matches + (same ? ", with every rip target the same: " + kept : ""));
		if (!targets.isEmpty()) {
			emit("Targets of rip0, most matches first:");
			targets.entrySet().stream()
					.sorted((a, b) -> b.getValue() - a.getValue())
					.limit(20)
					.forEach(e -> emit("  rva 0x" + Long.toHexString(e.getKey() - imageBase.getOffset())
						+ "  (" + toAddr(e.getKey()) + ")  " + e.getValue() + " matches"));
		}
		write(out);
	}

	private byte[] readBlock(MemoryBlock block) throws Exception {
		long size = block.getSize();
		if (size > Integer.MAX_VALUE) {
			throw new IllegalStateException("Block too large: " + block.getName());
		}
		byte[] bytes = new byte[(int) size];
		int read = block.getBytes(block.getStart(), bytes, 0, bytes.length);
		if (read < bytes.length) {
			byte[] trimmed = new byte[read];
			System.arraycopy(bytes, 0, trimmed, 0, read);
			return trimmed;
		}
		return bytes;
	}

	static boolean matchesAt(byte[] bytes, int at, byte[] pattern, byte[] mask) {
		for (int j = 0; j < pattern.length; j++) {
			if ((bytes[at + j] & mask[j]) != pattern[j]) {
				return false;
			}
		}
		return true;
	}

	static int readInt32(byte[] b, int at) {
		return (b[at] & 0xFF) | (b[at + 1] & 0xFF) << 8 | (b[at + 2] & 0xFF) << 16 | (b[at + 3] & 0xFF) << 24;
	}

	static byte[][] parsePattern(String text) {
		String[] tokens = text.trim().split("\\s+");
		byte[] pattern = new byte[tokens.length];
		byte[] mask = new byte[tokens.length];
		for (int i = 0; i < tokens.length; i++) {
			String t = tokens[i];
			if (t.equals("?")) {
				t = "??";
			}
			if (t.length() != 2) {
				throw new IllegalArgumentException("Bad pattern byte '" + tokens[i] + "'");
			}
			int value = 0;
			int m = 0;
			for (int n = 0; n < 2; n++) {
				char c = t.charAt(n);
				value <<= 4;
				m <<= 4;
				if (c != '?') {
					value |= Character.digit(c, 16);
					m |= 0xF;
				}
			}
			pattern[i] = (byte) value;
			mask[i] = (byte) m;
		}
		return new byte[][] { pattern, mask };
	}

	private static int toInt(String s) {
		s = s.trim();
		return s.toLowerCase().startsWith("0x") ? Integer.parseInt(s.substring(2), 16) : Integer.parseInt(s);
	}

	private void emit(String line) {
		println(line);
		report.add(line);
	}

	private void write(String out) throws Exception {
		if (out == null) {
			return;
		}
		File file = new File(out);
		if (file.getParentFile() != null) {
			file.getParentFile().mkdirs();
		}
		try (PrintWriter w = new PrintWriter(file, "UTF-8")) {
			report.forEach(w::println);
		}
		println("Wrote " + file.getAbsolutePath());
	}
}
