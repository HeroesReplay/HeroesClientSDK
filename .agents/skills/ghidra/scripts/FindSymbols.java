// Lists symbols whose full name (with namespaces) contains some text: RTTI classes and their
// vftables (from the Windows x86 PE RTTI Analyzer), demangled functions, imports and labels.
// An MSVC game binary has no debug names, but RTTI gives class names such as
// "SomeScreenClass::vftable", which is a quick way into a screen or dialog object.
//
// Usage (headless):
//   -postScript FindSymbols.java Screen
//   -postScript FindSymbols.java "::vftable" filter:Loading max:100 out:C:\re\vtables.txt
//
// Arguments:
//   <text>          substring of the full symbol name (case-insensitive)
//   filter:<text>   a second substring that must also match
//   regex           treat <text> as a Java regular expression
//   max:<n>         print at most n symbols (default 300)
//   out:<file>      also write the report to this file
//
// Needs an analyzed program (RTTI and demangler analyzers on).
//@category HeroesClientSDK

import java.io.File;
import java.io.PrintWriter;
import java.util.ArrayList;
import java.util.List;
import java.util.regex.Pattern;

import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.symbol.Symbol;
import ghidra.program.model.symbol.SymbolIterator;

public class FindSymbols extends GhidraScript {

	private final List<String> report = new ArrayList<>();

	@Override
	protected void run() throws Exception {
		String[] args = getScriptArgs();
		if (args.length == 0) {
			printerr("Usage: FindSymbols.java <text> [filter:<text>] [regex] [max:<n>] [out:<file>]");
			return;
		}
		String text = null;
		String filter = null;
		boolean regex = false;
		int max = 300;
		String out = null;
		for (String arg : args) {
			String lower = arg.toLowerCase();
			if (lower.startsWith("filter:")) {
				filter = arg.substring(7).toLowerCase();
			}
			else if (lower.equals("regex")) {
				regex = true;
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
		Pattern pattern = regex ? Pattern.compile(text, Pattern.CASE_INSENSITIVE) : null;
		String needle = text.toLowerCase();
		Address imageBase = currentProgram.getImageBase();

		int count = 0;
		SymbolIterator it = currentProgram.getSymbolTable().getAllSymbols(true);
		while (it.hasNext()) {
			monitor.checkCancelled();
			Symbol s = it.next();
			String name = s.getName(true);
			String lower = name.toLowerCase();
			boolean hit = regex ? pattern.matcher(name).find() : lower.contains(needle);
			if (!hit || (filter != null && !lower.contains(filter))) {
				continue;
			}
			count++;
			if (count <= max) {
				Address a = s.getAddress();
				String rva = a.isMemoryAddress() ? "rva 0x" + Long.toHexString(a.subtract(imageBase)) : "external";
				emit(String.format("%s  %s  %s  %s  refs %d", a, rva, s.getSymbolType(), name, s.getReferenceCount()));
			}
		}
		emit("Symbols: " + count + (count > max ? " (printed " + max + ")" : ""));
		write(out);
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
