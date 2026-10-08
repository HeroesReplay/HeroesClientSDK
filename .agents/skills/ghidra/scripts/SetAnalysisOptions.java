// Lists or sets auto-analysis options. Use it as a -preScript so the options apply to the
// analysis that headless runs after import.
//
// Usage (headless):
//   -preScript SetAnalysisOptions.java list
//   -preScript SetAnalysisOptions.java preset:large-x64
//   -preScript SetAnalysisOptions.java "Decompiler Switch Analysis:false" "Function ID:false"
//
// Arguments:
//   list               print every analysis option and its current value, change nothing
//   preset:large-x64   turn off analyzers that cost a lot of time on a 40-80 MB x64 binary and
//                      add little for finding globals (see PRESET below)
//   preset:data-only   turn off every analyzer except strings, RTTI, demangler and data
//                      archives (for an exe whose code is encrypted on disk)
//   <option>:<value>   set one option; the option name is everything before the LAST colon
//
// Quote any argument with a space, '=', ',' or ';' in it: analyzeHeadless.bat splits on them.
//@category HeroesClientSDK

import java.util.LinkedHashMap;
import java.util.Map;

import ghidra.app.script.GhidraScript;

public class SetAnalysisOptions extends GhidraScript {

	// Off for a large x64 game binary. Everything else keeps Ghidra 12.1's default.
	// - Decompiler Parameter ID: runs the decompiler on every function to guess signatures.
	//   On by default for x64 PE and by far the slowest analyzer on a big binary. ExportFunction
	//   decompiles the few functions you care about on demand instead.
	// - Function ID: matches MSVC library functions; slow, little value for game globals.
	// - Aggressive Instruction Finder: guesses code in gaps; slow and noisy (default off).
	// - Embedded Media, WindowsResourceReference: resource scanning, no use here.
	// - PDB Universal: there is no PDB for the client.
	// - Condense Filler Bytes: cosmetic (default off).
	// Decompiler Switch Analysis stays on (switches on screen or dialog ids are useful); it is
	// the next one to turn off if analysis is still too slow.
	private static final String[][] PRESET = {
		{ "Decompiler Parameter ID", "false" },
		{ "Function ID", "false" },
		{ "Aggressive Instruction Finder", "false" },
		{ "Embedded Media", "false" },
		{ "WindowsResourceReference", "false" },
		{ "PDB Universal", "false" },
		{ "Condense Filler Bytes", "false" },
	};

	private static final java.util.Set<String> DATA_ONLY_KEEP = java.util.Set.of(
		"ASCII Strings", "Windows x86 PE RTTI Analyzer", "Demangler Microsoft", "Apply Data Archives");

	@Override
	protected void run() throws Exception {
		String[] args = getScriptArgs();
		if (currentProgram == null) {
			printerr("No program is open.");
			return;
		}
		if (args.length == 0 || "list".equalsIgnoreCase(args[0])) {
			list();
			return;
		}

		Map<String, String> current = getCurrentAnalysisOptionsAndValues(currentProgram);
		Map<String, String> wanted = new LinkedHashMap<>();
		for (String arg : args) {
			if ("preset:large-x64".equalsIgnoreCase(arg)) {
				for (String[] pair : PRESET) {
					wanted.put(pair[0], pair[1]);
				}
				continue;
			}
			if ("preset:data-only".equalsIgnoreCase(arg)) {
				// Every analyzer off except the ones that read data: strings, RTTI (class names
				// and vftables), the demangler and data type archives. For an exe whose code is
				// encrypted on disk: nothing disassembles ciphertext.
				for (Map.Entry<String, String> e : current.entrySet()) {
					String key = e.getKey();
					boolean analyzer = !key.contains(".")
						&& ("true".equalsIgnoreCase(e.getValue()) || "false".equalsIgnoreCase(e.getValue()));
					if (analyzer) {
						wanted.put(key, DATA_ONLY_KEEP.contains(key) ? "true" : "false");
					}
				}
				continue;
			}
			int colon = arg.lastIndexOf(':');
			if (colon <= 0) {
				printerr("Ignoring '" + arg + "': expected <option>:<value>, list, or preset:large-x64");
				continue;
			}
			wanted.put(arg.substring(0, colon), arg.substring(colon + 1));
		}

		for (Map.Entry<String, String> e : wanted.entrySet()) {
			if (!current.containsKey(e.getKey())) {
				println("skip (no such option in this Ghidra): " + e.getKey());
				continue;
			}
			setAnalysisOption(currentProgram, e.getKey(), e.getValue());
			println("set " + e.getKey() + " = " + e.getValue());
		}
	}

	private void list() {
		Map<String, String> options = getCurrentAnalysisOptionsAndValues(currentProgram);
		for (Map.Entry<String, String> e : new java.util.TreeMap<>(options).entrySet()) {
			println(e.getKey() + " = " + e.getValue());
		}
	}
}
