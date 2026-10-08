// Finds every instruction that names a global through a RIP-relative operand, for example
// `mov rcx,[G]`, `lea rdx,[G]` or `cmp byte ptr [G],0`. It scans the bytes of executable blocks
// for a rel32 displacement that lands on the global, then disassembles the candidate to confirm
// it, so it works on a program imported with -noanalysis too (no references needed).
//
// Usage (headless):
//   -postScript FindRipRelativeLoads.java rva:0x3771830
//   -postScript FindRipRelativeLoads.java rva:0x3771830 mnemonic:MOV operand:1 context:4
//
// Arguments:
//   <address>        rva:0x<hex>, 0x<hex> (absolute), or a symbol name
//   span:<n>         also accept targets in [address, address + n)
//   mnemonic:<m>     keep only this mnemonic (MOV, LEA, CMP, ...)
//   operand:<i>      keep only uses where the global is operand i (MOV: 1 = load, 0 = store)
//   context:<n>      print n instructions after each hit (and n before when the listing has
//                    them)
//   max:<n>          print at most n hits (default 500)
//   out:<file>       also write the report to this file
//@category HeroesClientSDK

import java.io.File;
import java.io.PrintWriter;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.TreeMap;

import ghidra.app.script.GhidraScript;
import ghidra.app.util.PseudoDisassembler;
import ghidra.app.util.PseudoInstruction;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.Instruction;
import ghidra.program.model.mem.MemoryBlock;
import ghidra.program.model.scalar.Scalar;
import ghidra.program.model.symbol.Symbol;

public class FindRipRelativeLoads extends GhidraScript {

	private final List<String> report = new ArrayList<>();

	@Override
	protected void run() throws Exception {
		String[] args = getScriptArgs();
		if (args.length == 0) {
			printerr("Usage: FindRipRelativeLoads.java <rva:0x..|0x..|symbol> [span:<n>] [mnemonic:<m>] [operand:<i>] [context:<n>] [max:<n>] [out:<file>]");
			return;
		}
		String targetText = null;
		long span = 1;
		String mnemonic = null;
		int operand = -1;
		int context = 0;
		int max = 500;
		String out = null;
		for (String arg : args) {
			String lower = arg.toLowerCase();
			if (lower.startsWith("span:")) {
				span = toLong(arg.substring(5));
			}
			else if (lower.startsWith("mnemonic:")) {
				mnemonic = arg.substring(9).toUpperCase();
			}
			else if (lower.startsWith("operand:")) {
				operand = (int) toLong(arg.substring(8));
			}
			else if (lower.startsWith("context:")) {
				context = (int) toLong(arg.substring(8));
			}
			else if (lower.startsWith("max:")) {
				max = (int) toLong(arg.substring(4));
			}
			else if (lower.startsWith("out:")) {
				out = arg.substring(4);
			}
			else if (targetText == null) {
				targetText = arg;
			}
		}

		Address target = resolve(targetText);
		if (target == null) {
			printerr("Cannot resolve address or symbol: " + targetText);
			return;
		}
		Address imageBase = currentProgram.getImageBase();
		long lo = target.getOffset();
		long hi = lo + span;
		emit("RIP-relative uses of " + target + " (rva 0x" + Long.toHexString(target.subtract(imageBase)) + ")"
			+ (span > 1 ? " span 0x" + Long.toHexString(span) : "")
			+ (mnemonic != null ? " mnemonic " + mnemonic : "")
			+ (operand >= 0 ? " operand " + operand : ""));

		PseudoDisassembler disassembler = new PseudoDisassembler(currentProgram);
		int hits = 0;
		Map<String, Integer> byFunction = new TreeMap<>();
		for (MemoryBlock block : currentProgram.getMemory().getBlocks()) {
			if (!block.isInitialized() || !block.isExecute()) {
				continue;
			}
			byte[] bytes = new byte[(int) block.getSize()];
			int length = block.getBytes(block.getStart(), bytes, 0, bytes.length);
			long base = block.getStart().getOffset();
			long lastStart = -1;
			for (int i = 1; i + 4 <= length; i++) {
				// A RIP-relative operand is a ModRM byte with mod=00, r/m=101, then disp32.
				if ((bytes[i - 1] & 0xC7) != 0x05) {
					continue;
				}
				long disp = readInt32(bytes, i);
				// The instruction ends 0-4 bytes after the displacement (a trailing immediate).
				for (int k = 0; k <= 4; k++) {
					long end = base + i + 4 + k;
					long t = end + disp;
					if (t < lo || t >= hi) {
						continue;
					}
					monitor.checkCancelled();
					Instruction ins = instructionAt(base, i, disassembler);
					if (ins == null || ins.getAddress().getOffset() <= lastStart
						|| ins.getAddress().getOffset() + ins.getLength() != end) {
						continue;
					}
					int op = operandNaming(ins, t);
					if (op < 0 || (operand >= 0 && op != operand)
						|| (mnemonic != null && !ins.getMnemonicString().equalsIgnoreCase(mnemonic))) {
						continue;
					}
					lastStart = ins.getAddress().getOffset();
					hits++;
					Address at = ins.getAddress();
					Function f = getFunctionContaining(at);
					String fn = f == null ? "(no function)"
							: f.getName() + " (rva 0x" + Long.toHexString(f.getEntryPoint().subtract(imageBase)) + ")";
					byFunction.merge(fn, 1, Integer::sum);
					if (hits <= max) {
						emit(at + "  rva 0x" + Long.toHexString(at.subtract(imageBase)) + "  op" + op + "  "
							+ ins + "  in " + fn);
						printContext(at, ins, context, disassembler);
					}
					break;
				}
			}
		}
		emit("Hits: " + hits + " in " + byFunction.size() + " functions");
		byFunction.entrySet().stream()
				.sorted((a, b) -> b.getValue() - a.getValue())
				.limit(50)
				.forEach(e -> emit("  " + e.getValue() + "  " + e.getKey()));
		write(out);
	}

	/**
	 * The instruction that holds the displacement at base + i. The listing decides when the
	 * program was analyzed. Otherwise linear sweeps that start 8-24 bytes earlier vote on the
	 * instruction boundary (x86 decoding resynchronises within a few instructions), so a REX or
	 * prefix byte is neither dropped nor borrowed from the previous instruction.
	 */
	private Instruction instructionAt(long base, int i, PseudoDisassembler disassembler) {
		Address disp = toAddr(base + i);
		Instruction known = currentProgram.getListing().getInstructionContaining(disp);
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

	/** The operand index that names target, or -1. */
	private int operandNaming(Instruction ins, long target) {
		for (int i = 0; i < ins.getNumOperands(); i++) {
			for (Object o : ins.getOpObjects(i)) {
				if (o instanceof Address a && a.getOffset() == target) {
					return i;
				}
				if (o instanceof Scalar s && s.getUnsignedValue() == target) {
					return i;
				}
			}
		}
		return -1;
	}

	private void printContext(Address at, Instruction hit, int context, PseudoDisassembler disassembler) {
		if (context <= 0) {
			return;
		}
		List<String> before = new ArrayList<>();
		Instruction prev = getInstructionBefore(at);
		while (prev != null && before.size() < context) {
			before.add(0, "      " + prev.getAddress() + "  " + prev);
			prev = getInstructionBefore(prev.getAddress());
		}
		before.forEach(this::emit);
		emit("   >> " + at + "  " + hit);
		Address next = at.add(hit.getLength());
		for (int n = 0; n < context; n++) {
			try {
				PseudoInstruction ins = disassembler.disassemble(next);
				emit("      " + next + "  " + ins);
				next = next.add(ins.getLength());
			}
			catch (Exception e) {
				break;
			}
		}
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
