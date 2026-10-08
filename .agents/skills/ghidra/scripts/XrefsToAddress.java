// Lists the references Ghidra's analysis recorded to an address (usually a global), with the
// instruction and the containing function, then a per-function summary.
//
// Usage (headless):
//   -postScript XrefsToAddress.java rva:0x3771830
//   -postScript XrefsToAddress.java 0x143771830 span:0x220 out:C:\re\xrefs.txt
//
// Arguments:
//   <address>     rva:0x<hex> (relative to the image base), 0x<hex> (absolute), or a symbol name
//   span:<n>      include references to [address, address + n), for fields of a global struct
//   max:<n>       print at most n references (default 500)
//   out:<file>    also write the report to this file
//
// Needs an analyzed program: references come from auto-analysis. For an import with
// -noanalysis, use FindRipRelativeLoads, which reads operands directly.
//@category HeroesClientSDK

import java.io.File;
import java.io.PrintWriter;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.TreeMap;

import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.CodeUnit;
import ghidra.program.model.listing.Function;
import ghidra.program.model.symbol.Reference;
import ghidra.program.model.symbol.ReferenceIterator;
import ghidra.program.model.symbol.Symbol;

public class XrefsToAddress extends GhidraScript {

	private final List<String> report = new ArrayList<>();

	@Override
	protected void run() throws Exception {
		String[] args = getScriptArgs();
		if (args.length == 0) {
			printerr("Usage: XrefsToAddress.java <rva:0x..|0x..|symbol> [span:<n>] [max:<n>] [out:<file>]");
			return;
		}
		String target = null;
		long span = 1;
		int max = 500;
		String out = null;
		for (String arg : args) {
			String lower = arg.toLowerCase();
			if (lower.startsWith("span:")) {
				span = toLong(arg.substring(5));
			}
			else if (lower.startsWith("max:")) {
				max = (int) toLong(arg.substring(4));
			}
			else if (lower.startsWith("out:")) {
				out = arg.substring(4);
			}
			else if (target == null) {
				target = arg;
			}
		}

		Address start = resolve(target);
		if (start == null) {
			printerr("Cannot resolve address or symbol: " + target);
			return;
		}
		Address imageBase = currentProgram.getImageBase();
		emit("References to " + start + " (rva 0x" + Long.toHexString(start.subtract(imageBase)) + ")"
			+ (span > 1 ? " span 0x" + Long.toHexString(span) : ""));

		int count = 0;
		Map<String, Integer> byFunction = new TreeMap<>();
		for (long k = 0; k < span; k++) {
			Address to = start.add(k);
			ReferenceIterator refs = currentProgram.getReferenceManager().getReferencesTo(to);
			while (refs.hasNext()) {
				monitor.checkCancelled();
				Reference ref = refs.next();
				Address from = ref.getFromAddress();
				count++;
				Function f = getFunctionContaining(from);
				String fn = f == null ? "(no function)"
						: f.getName() + " (rva 0x" + Long.toHexString(f.getEntryPoint().subtract(imageBase)) + ")";
				byFunction.merge(fn, 1, Integer::sum);
				if (count <= max) {
					CodeUnit cu = currentProgram.getListing().getCodeUnitContaining(from);
					String text = cu == null ? "" : cu.toString();
					emit(from + "  rva 0x" + Long.toHexString(from.subtract(imageBase))
						+ (k > 0 ? "  [+0x" + Long.toHexString(k) + "]" : "")
						+ "  " + ref.getReferenceType() + "  " + text + "  in " + fn);
				}
			}
		}
		emit("References: " + count + " from " + byFunction.size() + " functions");
		byFunction.entrySet().stream()
				.sorted((a, b) -> b.getValue() - a.getValue())
				.forEach(e -> emit("  " + e.getValue() + "  " + e.getKey()));
		if (count == 0) {
			emit("No references. Was the program analyzed? Try FindRipRelativeLoads.java.");
		}
		write(out);
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
