// Builds a wildcarded byte signature that starts at an instruction and grows one instruction at
// a time until it matches exactly once in the executable blocks (.text). Addresses that move
// between builds are wildcarded: RIP-relative displacements, rel8/rel32 branch and call
// targets, and absolute addresses. With "wildoffsets", 32-bit struct offsets and immediates are
// wildcarded too (an offset of 0x80 or more is always a disp32 in x64).
//
// Usage (headless):
//   -postScript MakeSignature.java rva:0x1234567
//   -postScript MakeSignature.java 0x141234567 insns:16 wildoffsets out:C:\re\sig.txt
//
// Arguments:
//   <address>      instruction start: rva:0x<hex>, 0x<hex> (absolute), or a symbol name
//   insns:<n>      give up after n instructions (default 16)
//   min:<n>        do not test uniqueness before n bytes (default 8)
//   wildoffsets    also wildcard 32-bit displacements and immediates
//   out:<file>     also write the report to this file
//
// The report gives the signature, each wildcard group with its offset and what it pointed at
// (so "rip disp at +3, instruction end at +7" can go straight into an SDK pattern), the
// instructions it covers, and the C# byte and mask arrays in the style of MatchClockPattern.
//@category HeroesClientSDK

import java.io.File;
import java.io.PrintWriter;
import java.util.ArrayList;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Set;

import ghidra.app.script.GhidraScript;
import ghidra.app.util.PseudoDisassembler;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Instruction;
import ghidra.program.model.mem.MemoryBlock;
import ghidra.program.model.scalar.Scalar;
import ghidra.program.model.symbol.Reference;
import ghidra.program.model.symbol.Symbol;

public class MakeSignature extends GhidraScript {

	private final List<String> report = new ArrayList<>();
	private final List<Address> blockStarts = new ArrayList<>();
	private final List<byte[]> blockBytes = new ArrayList<>();
	private int lastRipPos = -1;
	private long lastRipTarget;
	private int ripDisp = -1;
	private int ripEnd = -1;
	private long ripTarget;

	@Override
	protected void run() throws Exception {
		String[] args = getScriptArgs();
		if (args.length == 0) {
			printerr("Usage: MakeSignature.java <rva:0x..|0x..|symbol> [insns:<n>] [min:<n>] [wildoffsets] [out:<file>]");
			return;
		}
		String startText = null;
		int maxInsns = 16;
		int minBytes = 8;
		boolean wildOffsets = false;
		String out = null;
		for (String arg : args) {
			String lower = arg.toLowerCase();
			if (lower.startsWith("insns:")) {
				maxInsns = Integer.parseInt(arg.substring(6));
			}
			else if (lower.startsWith("min:")) {
				minBytes = Integer.parseInt(arg.substring(4));
			}
			else if (lower.equals("wildoffsets")) {
				wildOffsets = true;
			}
			else if (lower.startsWith("out:")) {
				out = arg.substring(4);
			}
			else if (startText == null) {
				startText = arg;
			}
		}
		Address start = resolve(startText);
		if (start == null) {
			printerr("Cannot resolve address or symbol: " + startText);
			return;
		}
		Address imageBase = currentProgram.getImageBase();
		for (MemoryBlock block : currentProgram.getMemory().getBlocks()) {
			if (block.isInitialized() && block.isExecute()) {
				byte[] bytes = new byte[(int) block.getSize()];
				int read = block.getBytes(block.getStart(), bytes, 0, bytes.length);
				if (read < bytes.length) {
					byte[] trimmed = new byte[read];
					System.arraycopy(bytes, 0, trimmed, 0, read);
					bytes = trimmed;
				}
				blockStarts.add(block.getStart());
				blockBytes.add(bytes);
			}
		}

		PseudoDisassembler disassembler = new PseudoDisassembler(currentProgram);
		List<Byte> sig = new ArrayList<>();
		List<Boolean> keep = new ArrayList<>();
		List<String> groups = new ArrayList<>();
		List<String> listing = new ArrayList<>();
		Address at = start;
		int matches = -1;
		List<Address> found = new ArrayList<>();
		for (int n = 0; n < maxInsns; n++) {
			Instruction ins = currentProgram.getListing().getInstructionAt(at);
			if (ins == null) {
				ins = disassembler.disassemble(at);
			}
			int offset = sig.size();
			byte[] bytes = ins.getBytes();
			boolean[] wild = new boolean[bytes.length];
			lastRipPos = -1;
			for (String g : wildcard(ins, bytes, wild, wildOffsets, imageBase, offset)) {
				groups.add(g);
			}
			if (ripDisp < 0 && lastRipPos >= 0) {
				ripDisp = offset + lastRipPos;
				ripEnd = offset + bytes.length;
				ripTarget = lastRipTarget;
			}
			for (int i = 0; i < bytes.length; i++) {
				sig.add(bytes[i]);
				keep.add(!wild[i]);
			}
			listing.add(String.format("  +%-3d %s  %s", offset, at, ins));
			at = at.add(bytes.length);
			if (sig.size() < minBytes) {
				continue;
			}
			found = search(sig, keep, 1000);
			matches = found.size();
			if (matches <= 1) {
				break;
			}
		}

		emit("Signature for " + start + " (rva 0x" + Long.toHexString(start.subtract(imageBase)) + "), "
			+ sig.size() + " bytes, " + listing.size() + " instructions");
		emit(render(sig, keep));
		emit("Matches in executable blocks: " + matches + (matches == 1 ? " (unique)" : matches == 0 ? " (none: check the start address)" : " (NOT unique; raise insns or start elsewhere)"));
		if (matches > 1) {
			found.stream().limit(10).forEach(a -> emit("  also at " + a + " rva 0x" + Long.toHexString(a.subtract(imageBase))));
		}
		if (ripDisp >= 0 && matches >= 1) {
			// The SDK can also accept a shape that is not unique when its sites agree on the
			// global (LoadingScreenPattern does this), so count the matches that name it.
			int agree = 0;
			for (Address a : found) {
				long t = a.getOffset() + ripEnd + currentProgram.getMemory().getInt(a.add(ripDisp));
				if (t == ripTarget) {
					agree++;
				}
			}
			emit("Matches naming the same global (rva 0x" + Long.toHexString(ripTarget - imageBase.getOffset())
				+ ", rip disp at +" + ripDisp + ", instruction end at +" + ripEnd + "): " + agree + " of " + matches);
		}
		emit("Wildcards (offset, size, kind, target):");
		groups.forEach(g -> emit("  " + g));
		emit("Instructions:");
		listing.forEach(this::emit);
		emit("C# pattern: " + csharpBytes(sig, keep));
		emit("C# mask:    " + csharpMask(keep));
		write(out);
	}

	/** Marks the bytes of ins that hold addresses (or offsets) and describes each group. */
	private List<String> wildcard(Instruction ins, byte[] b, boolean[] wild, boolean wildOffsets, Address imageBase,
			int offset) {
		List<String> groups = new ArrayList<>();
		long end = ins.getAddress().getOffset() + b.length;
		Set<Long> targets = new LinkedHashSet<>();
		Set<Long> scalars = new LinkedHashSet<>();
		for (int i = 0; i < ins.getNumOperands(); i++) {
			for (Object o : ins.getOpObjects(i)) {
				if (o instanceof Address a) {
					targets.add(a.getOffset());
				}
				else if (o instanceof Scalar s) {
					long v = s.getUnsignedValue();
					if (isProgramAddress(v)) {
						targets.add(v);
					}
					else {
						scalars.add(s.getSignedValue());
					}
				}
			}
			for (Reference r : ins.getOperandReferences(i)) {
				if (r.getToAddress().isMemoryAddress()) {
					targets.add(r.getToAddress().getOffset());
				}
			}
		}
		for (Address flow : ins.getFlows()) {
			targets.add(flow.getOffset());
		}

		for (long t : targets) {
			long rel = t - end;
			String rva = "rva 0x" + Long.toHexString(t - imageBase.getOffset());
			boolean done = false;
			if (rel >= Integer.MIN_VALUE && rel <= Integer.MAX_VALUE) {
				for (int p = b.length - 4; p >= 1 && !done; p--) {
					if (readInt32(b, p) == (int) rel && !any(wild, p, 4)) {
						mark(wild, p, 4);
						boolean branch = ins.getFlowType().isJump() || ins.getFlowType().isCall();
						groups.add("+" + (offset + p) + " size 4 " + (branch ? "rel32" : "rip") + " -> " + rva
							+ " (instruction +" + offset + " ends at +" + (offset + b.length) + ")");
						if (!branch && lastRipPos < 0) {
							lastRipPos = p;
							lastRipTarget = t;
						}
						done = true;
					}
				}
			}
			if (!done && rel >= Byte.MIN_VALUE && rel <= Byte.MAX_VALUE && ins.getFlowType().isJump()) {
				int p = b.length - 1;
				if (b[p] == (byte) rel && !wild[p]) {
					mark(wild, p, 1);
					groups.add("+" + (offset + p) + " size 1 rel8 -> " + rva);
					done = true;
				}
			}
			if (!done) {
				for (int p = b.length - 8; p >= 0 && !done; p--) {
					if (readInt64(b, p) == t && !any(wild, p, 8)) {
						mark(wild, p, 8);
						groups.add("+" + (offset + p) + " size 8 abs64 -> " + rva);
						done = true;
					}
				}
			}
			if (!done && t <= 0xFFFFFFFFL) {
				for (int p = b.length - 4; p >= 0 && !done; p--) {
					if ((readInt32(b, p) & 0xFFFFFFFFL) == t && !any(wild, p, 4)) {
						mark(wild, p, 4);
						groups.add("+" + (offset + p) + " size 4 abs32 -> " + rva);
						done = true;
					}
				}
			}
		}
		if (wildOffsets) {
			for (long v : scalars) {
				if (Math.abs(v) < 0x80 || v < Integer.MIN_VALUE || v > 0xFFFFFFFFL) {
					continue;
				}
				for (int p = b.length - 4; p >= 1; p--) {
					if (readInt32(b, p) == (int) v && !any(wild, p, 4)) {
						mark(wild, p, 4);
						groups.add("+" + (offset + p) + " size 4 offset/imm 0x" + Long.toHexString(v));
						break;
					}
				}
			}
		}
		return groups;
	}

	private boolean isProgramAddress(long v) {
		try {
			return currentProgram.getMemory().contains(toAddr(v));
		}
		catch (Exception e) {
			return false;
		}
	}

	private List<Address> search(List<Byte> sig, List<Boolean> keep, int limit) {
		int n = sig.size();
		byte[] p = new byte[n];
		boolean[] k = new boolean[n];
		for (int i = 0; i < n; i++) {
			p[i] = sig.get(i);
			k[i] = keep.get(i);
		}
		List<Address> hits = new ArrayList<>();
		for (int b = 0; b < blockBytes.size(); b++) {
			byte[] bytes = blockBytes.get(b);
			outer: for (int i = 0; i + n <= bytes.length; i++) {
				for (int j = 0; j < n; j++) {
					if (k[j] && bytes[i + j] != p[j]) {
						continue outer;
					}
				}
				hits.add(blockStarts.get(b).add(i));
				if (hits.size() >= limit) {
					return hits;
				}
			}
		}
		return hits;
	}

	private static String render(List<Byte> sig, List<Boolean> keep) {
		StringBuilder s = new StringBuilder();
		for (int i = 0; i < sig.size(); i++) {
			if (i > 0) {
				s.append(' ');
			}
			s.append(keep.get(i) ? String.format("%02X", sig.get(i) & 0xFF) : "??");
		}
		return s.toString();
	}

	private static String csharpBytes(List<Byte> sig, List<Boolean> keep) {
		StringBuilder s = new StringBuilder("{ ");
		for (int i = 0; i < sig.size(); i++) {
			s.append(i > 0 ? ", " : "").append(String.format("0x%02X", keep.get(i) ? sig.get(i) & 0xFF : 0));
		}
		return s.append(" }").toString();
	}

	private static String csharpMask(List<Boolean> keep) {
		StringBuilder s = new StringBuilder("{ ");
		for (int i = 0; i < keep.size(); i++) {
			s.append(i > 0 ? ", " : "").append(keep.get(i) ? "1" : "0");
		}
		return s.append(" }").toString();
	}

	private static boolean any(boolean[] w, int at, int len) {
		for (int i = at; i < at + len; i++) {
			if (w[i]) {
				return true;
			}
		}
		return false;
	}

	private static void mark(boolean[] w, int at, int len) {
		for (int i = at; i < at + len; i++) {
			w[i] = true;
		}
	}

	private static int readInt32(byte[] b, int at) {
		return (b[at] & 0xFF) | (b[at + 1] & 0xFF) << 8 | (b[at + 2] & 0xFF) << 16 | (b[at + 3] & 0xFF) << 24;
	}

	private static long readInt64(byte[] b, int at) {
		return (readInt32(b, at) & 0xFFFFFFFFL) | ((long) readInt32(b, at + 4)) << 32;
	}

	private Address resolve(String text) {
		if (text == null) {
			return null;
		}
		String lower = text.toLowerCase();
		try {
			if (lower.startsWith("rva:")) {
				return currentProgram.getImageBase().add(toLong(text.substring(4)));
			}
			if (lower.startsWith("0x")) {
				return toAddr(toLong(text));
			}
		}
		catch (Exception e) {
			return null;
		}
		for (Symbol s : currentProgram.getSymbolTable().getSymbols(text)) {
			return s.getAddress();
		}
		return null;
	}

	private static long toLong(String s) {
		s = s.trim();
		return s.toLowerCase().startsWith("0x") ? Long.parseUnsignedLong(s.substring(2), 16) : Long.parseLong(s);
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
