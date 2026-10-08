// Finds strings (ASCII or UTF-16) containing some text and the code that references them, with
// the containing function. Good for UI screen, dialog and frame names, error ids and log text.
//
// Usage (headless):
//   -postScript FindStringRefs.java "ScreenHome"
//   -postScript FindStringRefs.java "LoadingScreen" scan out:C:\re\loading.txt
//   -postScript FindStringRefs.java "^Screen[A-Z]" regex
//
// Arguments:
//   <text>        the text to look for (a substring; case-insensitive unless "case")
//   regex         treat <text> as a Java regular expression over defined strings only
//   case          case-sensitive
//   scan          also scan code for RIP-relative operands that name each string (finds uses
//                 analysis did not record, and works after -noanalysis) and data for 8-byte
//                 pointers to it (string tables)
//   max:<n>       stop after n strings (default 200)
//   out:<file>    also write the report to this file
//
// Without "regex", the raw bytes of every initialized non-executable block are searched too,
// so strings that analysis did not define are found.
//@category HeroesClientSDK

import java.io.File;
import java.io.PrintWriter;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.TreeMap;
import java.util.regex.Pattern;

import ghidra.app.script.GhidraScript;
import ghidra.app.util.PseudoDisassembler;
import ghidra.app.util.PseudoInstruction;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.CodeUnit;
import ghidra.program.model.listing.Data;
import ghidra.program.model.listing.DataIterator;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.Instruction;
import ghidra.program.model.mem.MemoryBlock;
import ghidra.program.model.scalar.Scalar;
import ghidra.program.model.symbol.Reference;
import ghidra.program.model.symbol.ReferenceIterator;

public class FindStringRefs extends GhidraScript {

	private final List<String> report = new ArrayList<>();

	@Override
	protected void run() throws Exception {
		String[] args = getScriptArgs();
		if (args.length == 0) {
			printerr("Usage: FindStringRefs.java <text> [regex] [case] [scan] [max:<n>] [out:<file>]");
			return;
		}
		String text = null;
		boolean regex = false;
		boolean caseSensitive = false;
		boolean scan = false;
		int max = 200;
		String out = null;
		for (String arg : args) {
			String lower = arg.toLowerCase();
			if (lower.equals("regex")) {
				regex = true;
			}
			else if (lower.equals("case")) {
				caseSensitive = true;
			}
			else if (lower.equals("scan")) {
				scan = true;
			}
			else if (lower.startsWith("max:")) {
				max = Integer.parseInt(arg.substring(4));
			}
			else if (lower.startsWith("out:")) {
				out = arg.substring(4);
			}
			else if (text == null) {
				text = arg;
			}
		}

		Address imageBase = currentProgram.getImageBase();
		Pattern pattern = regex ? Pattern.compile(text, caseSensitive ? 0 : Pattern.CASE_INSENSITIVE) : null;
		String needle = caseSensitive ? text : text.toLowerCase();

		// 1. Strings that analysis defined.
		Map<Long, String> strings = new TreeMap<>();
		DataIterator data = currentProgram.getListing().getDefinedData(true);
		while (data.hasNext() && strings.size() < max) {
			monitor.checkCancelled();
			Data d = data.next();
			if (!d.hasStringValue()) {
				continue;
			}
			Object value = d.getValue();
			String s = value == null ? "" : value.toString();
			boolean hit = regex ? pattern.matcher(s).find()
					: (caseSensitive ? s : s.toLowerCase()).contains(needle);
			if (hit) {
				strings.put(d.getAddress().getOffset(), s);
			}
		}

		// 2. Raw bytes, ASCII and UTF-16LE, for strings analysis did not define.
		if (!regex) {
			for (MemoryBlock block : currentProgram.getMemory().getBlocks()) {
				if (!block.isInitialized() || block.isExecute() || strings.size() >= max) {
					continue;
				}
				byte[] bytes = new byte[(int) block.getSize()];
				int length = block.getBytes(block.getStart(), bytes, 0, bytes.length);
				rawSearch(bytes, length, block.getStart().getOffset(), text, caseSensitive, false, strings, max);
				rawSearch(bytes, length, block.getStart().getOffset(), text, caseSensitive, true, strings, max);
			}
		}

		Map<Long, List<String>> scanned = scan ? scanCode(strings.keySet()) : new HashMap<>();

		Map<String, Integer> byFunction = new TreeMap<>();
		int withRefs = 0;
		for (Map.Entry<Long, String> e : strings.entrySet()) {
			Address at = toAddr(e.getKey());
			MemoryBlock block = currentProgram.getMemory().getBlock(at);
			emit(at + "  rva 0x" + Long.toHexString(at.subtract(imageBase)) + "  "
				+ (block == null ? "" : block.getName()) + "  \"" + escape(e.getValue()) + "\"");
			List<String> lines = new ArrayList<>();
			java.util.Set<Long> seen = new java.util.HashSet<>();
			ReferenceIterator refs = currentProgram.getReferenceManager().getReferencesTo(at);
			while (refs.hasNext()) {
				Reference ref = refs.next();
				Address from = ref.getFromAddress();
				if (seen.add(from.getOffset())) {
					lines.add(describe(from, imageBase, byFunction));
				}
			}
			for (String line : scanned.getOrDefault(e.getKey(), List.of())) {
				long from = Long.parseLong(line.substring(0, line.indexOf(' ')));
				if (seen.add(from)) {
					String rest = line.substring(line.indexOf(' ') + 1);
					lines.add(rest.startsWith("pointer") ? "    " + toAddr(from) + "  rva 0x"
						+ Long.toHexString(toAddr(from).subtract(imageBase)) + "  " + rest
							: describe(toAddr(from), imageBase, byFunction) + "  (scan)");
				}
			}
			if (!lines.isEmpty()) {
				withRefs++;
			}
			lines.forEach(this::emit);
		}
		emit("Strings: " + strings.size() + ", with references: " + withRefs
			+ (scan ? "" : " (add 'scan' to also scan code and pointer tables)"));
		emit("Functions referencing them:");
		byFunction.entrySet().stream()
				.sorted((a, b) -> b.getValue() - a.getValue())
				.limit(100)
				.forEach(f -> emit("  " + f.getValue() + "  " + f.getKey()));
		write(out);
	}

	private String describe(Address from, Address imageBase, Map<String, Integer> byFunction) {
		Function f = getFunctionContaining(from);
		String fn = f == null ? "(no function)"
				: f.getName() + " (rva 0x" + Long.toHexString(f.getEntryPoint().subtract(imageBase)) + ")";
		if (f != null) {
			byFunction.merge(fn, 1, Integer::sum);
		}
		CodeUnit cu = currentProgram.getListing().getCodeUnitContaining(from);
		String code = cu == null ? "" : cu.toString();
		if (cu == null || !(cu instanceof Instruction)) {
			try {
				PseudoInstruction pi = new PseudoDisassembler(currentProgram).disassemble(from);
				code = pi.toString();
			}
			catch (Exception e) {
				// leave the code unit text
			}
		}
		return "    " + from + "  rva 0x" + Long.toHexString(from.subtract(imageBase)) + "  " + code + "  in " + fn;
	}

	private void rawSearch(byte[] bytes, int length, long base, String text, boolean caseSensitive, boolean wide,
			Map<Long, String> strings, int max) {
		byte[] ascii = text.getBytes(StandardCharsets.US_ASCII);
		int step = wide ? 2 : 1;
		int n = ascii.length * step;
		outer: for (int i = 0; i + n <= length && strings.size() < max; i++) {
			for (int j = 0; j < ascii.length; j++) {
				int b = bytes[i + j * step] & 0xFF;
				int want = ascii[j] & 0xFF;
				if (wide && bytes[i + j * step + 1] != 0) {
					continue outer;
				}
				if (caseSensitive ? b != want : Character.toLowerCase(b) != Character.toLowerCase(want)) {
					continue outer;
				}
			}
			// Walk back to the start of the string and forward to its terminator.
			int start = i;
			while (start - step >= 0 && printable(bytes, start - step, wide)) {
				start -= step;
			}
			StringBuilder s = new StringBuilder();
			int p = start;
			while (p + step <= length && printable(bytes, p, wide) && s.length() < 256) {
				s.append((char) (bytes[p] & 0xFF));
				p += step;
			}
			strings.putIfAbsent(base + start, s.toString());
			i = Math.max(i, p - 1);
		}
	}

	private static boolean printable(byte[] b, int at, boolean wide) {
		int c = b[at] & 0xFF;
		if (c < 0x20 || c > 0x7E) {
			return false;
		}
		return !wide || (at + 1 < b.length && b[at + 1] == 0);
	}

	/**
	 * One pass over executable blocks for RIP-relative operands that name any of the strings,
	 * and over data blocks for 8-byte absolute pointers to them. Returns "from text" lines.
	 */
	private Map<Long, List<String>> scanCode(java.util.Set<Long> targets) throws Exception {
		Map<Long, List<String>> found = new HashMap<>();
		if (targets.isEmpty()) {
			return found;
		}
		PseudoDisassembler disassembler = new PseudoDisassembler(currentProgram);
		for (MemoryBlock block : currentProgram.getMemory().getBlocks()) {
			if (!block.isInitialized()) {
				continue;
			}
			byte[] bytes = new byte[(int) block.getSize()];
			int length = block.getBytes(block.getStart(), bytes, 0, bytes.length);
			long base = block.getStart().getOffset();
			if (block.isExecute()) {
				for (int i = 1; i + 4 <= length; i++) {
					if ((bytes[i - 1] & 0xC7) != 0x05) {
						continue;
					}
					long disp = readInt32(bytes, i);
					for (int k = 0; k <= 4; k++) {
						long end = base + i + 4 + k;
						long t = end + disp;
						if (!targets.contains(t)) {
							continue;
						}
						monitor.checkCancelled();
						Instruction ins = instructionAt(base, i, disassembler);
						if (ins == null || ins.getAddress().getOffset() + ins.getLength() != end || !names(ins, t)) {
							continue;
						}
						found.computeIfAbsent(t, x -> new ArrayList<>())
								.add(ins.getAddress().getOffset() + " " + ins);
						break;
					}
				}
			}
			else {
				for (int i = 0; i + 8 <= length; i += 8) {
					long v = (readInt32(bytes, i) & 0xFFFFFFFFL) | ((long) readInt32(bytes, i + 4)) << 32;
					if (targets.contains(v)) {
						found.computeIfAbsent(v, x -> new ArrayList<>())
								.add((base + i) + " pointer in " + block.getName() + " (string table?)");
					}
				}
			}
		}
		return found;
	}

	private boolean names(Instruction ins, long target) {
		for (int i = 0; i < ins.getNumOperands(); i++) {
			for (Object o : ins.getOpObjects(i)) {
				if (o instanceof Address a && a.getOffset() == target) {
					return true;
				}
				if (o instanceof Scalar s && s.getUnsignedValue() == target) {
					return true;
				}
			}
		}
		return false;
	}

	/** Same boundary rule as FindRipRelativeLoads: the listing, else voting linear sweeps. */
	private Instruction instructionAt(long base, int i, PseudoDisassembler disassembler) {
		Instruction known = currentProgram.getListing().getInstructionContaining(toAddr(base + i));
		if (known != null) {
			return known;
		}
		Map<Long, Integer> votes = new TreeMap<>();
		for (int s = 24; s >= 8; s--) {
			long p = base + i - s;
			if (p < base) {
				continue;
			}
			while (p <= base + i) {
				PseudoInstruction ins;
				try {
					ins = disassembler.disassemble(toAddr(p));
				}
				catch (Exception e) {
					break;
				}
				if (ins == null || ins.getLength() <= 0) {
					break;
				}
				if (p + ins.getLength() > base + i) {
					votes.merge(p, 1, Integer::sum);
					break;
				}
				p += ins.getLength();
			}
		}
		long best = -1;
		int bestVotes = 0;
		for (Map.Entry<Long, Integer> e : votes.entrySet()) {
			if (e.getValue() > bestVotes) {
				best = e.getKey();
				bestVotes = e.getValue();
			}
		}
		if (best < 0) {
			return null;
		}
		try {
			return disassembler.disassemble(toAddr(best));
		}
		catch (Exception e) {
			return null;
		}
	}

	private static int readInt32(byte[] b, int at) {
		return (b[at] & 0xFF) | (b[at + 1] & 0xFF) << 8 | (b[at + 2] & 0xFF) << 16 | (b[at + 3] & 0xFF) << 24;
	}

	private static String escape(String s) {
		String t = s.replace("\\", "\\\\").replace("\n", "\\n").replace("\r", "\\r").replace("\"", "\\\"");
		return t.length() > 160 ? t.substring(0, 160) + "..." : t;
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
