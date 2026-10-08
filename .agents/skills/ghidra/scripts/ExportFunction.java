// Decompiles functions to C and writes them to a file (and the console). Give the function's
// entry or any address inside it. With "asm", the disassembly follows each function.
//
// Usage (headless):
//   -postScript ExportFunction.java rva:0x1234567 out:C:\re\fn.c
//   -postScript ExportFunction.java rva:0x1234567 rva:0x1239abc asm timeout:120 out:C:\re\fns.c
//
// Arguments:
//   <address>...   one or more: rva:0x<hex>, 0x<hex> (absolute), or a function/symbol name
//   asm            append the disassembly of each function
//   timeout:<s>    decompiler timeout per function in seconds (default 60)
//   out:<file>     write the C to this file (otherwise console only)
//
// An address in code that has no function yet (an import with -noanalysis, or code analysis
// missed) gets a function created first. Run headless with -readOnly so that is not saved.
//@category HeroesClientSDK

import java.io.File;
import java.io.PrintWriter;
import java.util.ArrayList;
import java.util.List;

import ghidra.app.cmd.disassemble.DisassembleCommand;
import ghidra.app.cmd.function.CreateFunctionCmd;
import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileOptions;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.Instruction;
import ghidra.program.model.listing.InstructionIterator;
import ghidra.program.model.symbol.Symbol;

public class ExportFunction extends GhidraScript {

	private final List<String> report = new ArrayList<>();

	@Override
	protected void run() throws Exception {
		String[] args = getScriptArgs();
		if (args.length == 0) {
			printerr("Usage: ExportFunction.java <rva:0x..|0x..|name>... [asm] [timeout:<s>] [out:<file>]");
			return;
		}
		List<String> targets = new ArrayList<>();
		boolean asm = false;
		int timeout = 60;
		String out = null;
		for (String arg : args) {
			String lower = arg.toLowerCase();
			if (lower.equals("asm")) {
				asm = true;
			}
			else if (lower.startsWith("timeout:")) {
				timeout = Integer.parseInt(arg.substring(8));
			}
			else if (lower.startsWith("out:")) {
				out = arg.substring(4);
			}
			else {
				targets.add(arg);
			}
		}

		Address imageBase = currentProgram.getImageBase();
		DecompInterface decompiler = new DecompInterface();
		DecompileOptions options = new DecompileOptions();
		options.grabFromProgram(currentProgram);
		decompiler.setOptions(options);
		decompiler.toggleCCode(true);
		decompiler.toggleSyntaxTree(true);
		decompiler.setSimplificationStyle("decompile");
		if (!decompiler.openProgram(currentProgram)) {
			printerr("Decompiler did not start: " + decompiler.getLastMessage());
			return;
		}
		try {
			for (String target : targets) {
				Address at = resolve(target);
				if (at == null) {
					printerr("Cannot resolve address or symbol: " + target);
					continue;
				}
				Function f = getFunctionContaining(at);
				Address entry = f != null ? f.getEntryPoint() : at;
				if (f == null || getInstructionAt(entry) == null || f.getBody().getNumAddresses() <= 1) {
					// Without analysis nothing is disassembled, and the PE loader leaves 1-byte functions
					// at the .pdata starts: disassemble from the entry, then create or fix the body.
					new DisassembleCommand(entry, null, true).applyTo(currentProgram, monitor);
					if (f == null) {
						if (new CreateFunctionCmd(entry).applyTo(currentProgram, monitor)) {
							f = getFunctionAt(entry);
						}
					}
					else {
						CreateFunctionCmd.fixupFunctionBody(currentProgram, f, monitor);
					}
				}
				if (f == null) {
					printerr("No function at " + at + " and none could be created");
					continue;
				}
				emit("// " + f.getName() + " at " + f.getEntryPoint() + " (rva 0x"
					+ Long.toHexString(f.getEntryPoint().subtract(imageBase)) + "), "
					+ f.getBody().getNumAddresses() + " bytes");
				DecompileResults r = decompiler.decompileFunction(f, timeout, monitor);
				if (r == null || !r.decompileCompleted() || r.getDecompiledFunction() == null) {
					emit("// decompile failed: " + (r == null ? "no result" : r.getErrorMessage()));
				}
				else {
					emit(r.getDecompiledFunction().getC());
				}
				if (asm) {
					emit("/* disassembly");
					InstructionIterator it = currentProgram.getListing().getInstructions(f.getBody(), true);
					while (it.hasNext()) {
						Instruction ins = it.next();
						emit(String.format("  %s  rva 0x%x  %s", ins.getAddress(),
							ins.getAddress().subtract(imageBase), ins));
					}
					emit("*/");
				}
				emit("");
			}
		}
		finally {
			decompiler.dispose();
		}
		write(out);
	}

	private Address resolve(String text) {
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
		for (Function f : getGlobalFunctions(text)) {
			return f.getEntryPoint();
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
