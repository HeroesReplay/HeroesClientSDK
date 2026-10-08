// Finds MSVC RTTI for classes whose name contains some text and resolves each class's vftable:
// type descriptor (".?AVName@@") -> complete object locator (COL) -> vftable. It reads raw bytes
// only, so it works without analysis: on a -noanalysis import of a memory image, and on an exe
// whose code is encrypted on disk. On the 2.57.0.98348 exe file the CScreen* type descriptors
// are there but nothing in the plain sections points at them (no COL), so their vftables
// resolve only from a memory image. A type descriptor with no COL can still be named by code
// (dynamic_cast, typeid): FindRipRelativeLoads on its RVA finds that code in an image.
//
// Why: an object's first qword is its vftable. Read that qword live (Read-ClientMemory.ps1) and
// look its RVA up here to name the class, for example the screen object at [[G]+0x218].
//
// Usage (headless):
//   -postScript FindVftables.java CScreen
//   -postScript FindVftables.java CScreenHome exact slots:4 out:C:\re\vftables.txt
//
// Arguments:
//   <text>        substring of the class name (case-insensitive), as in ".?AV<name>@@"
//   exact         the class name must equal <text>
//   slots:<n>     also print the first n vftable slots (default 0)
//   max:<n>       stop after n classes (default 200)
//   out:<file>    also write the report to this file
//@category HeroesClientSDK

import java.io.File;
import java.io.PrintWriter;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.List;

import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.mem.MemoryBlock;

public class FindVftables extends GhidraScript {

	private final List<String> report = new ArrayList<>();
	private final List<long[]> ranges = new ArrayList<>();
	private final List<byte[]> data = new ArrayList<>();
	private final List<Boolean> exec = new ArrayList<>();

	@Override
	protected void run() throws Exception {
		String[] args = getScriptArgs();
		if (args.length == 0) {
			printerr("Usage: FindVftables.java <class name text> [exact] [slots:<n>] [max:<n>] [out:<file>]");
			return;
		}
		String text = null;
		boolean exact = false;
		int slots = 0;
		int max = 200;
		String out = null;
		for (String arg : args) {
			String lower = arg.toLowerCase();
			if (lower.equals("exact")) {
				exact = true;
			}
			else if (lower.startsWith("slots:")) {
				slots = Integer.parseInt(arg.substring(6));
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

		long imageBase = currentProgram.getImageBase().getOffset();
		for (MemoryBlock block : currentProgram.getMemory().getBlocks()) {
			// Every initialized block: the COLs and vftables that are missing from the plain sections
			// of the exe file may sit in what is encrypted .text on disk.
			if (block.isInitialized()) {
				byte[] bytes = new byte[(int) block.getSize()];
				int read = block.getBytes(block.getStart(), bytes, 0, bytes.length);
				ranges.add(new long[] { block.getStart().getOffset(), read });
				data.add(bytes);
				exec.add(block.isExecute());
			}
		}

		String needle = text.toLowerCase();
		int classes = 0;
		for (int b = 0; b < data.size() && classes < max; b++) {
			if (exec.get(b)) {
				continue;
			}
			byte[] bytes = data.get(b);
			long base = ranges.get(b)[0];
			int length = (int) ranges.get(b)[1];
			for (int i = 16; i + 8 < length && classes < max; i++) {
				// ".?AV" (class) or ".?AU" (struct) starts the decorated name of a type descriptor.
				if (bytes[i] != '.' || bytes[i + 1] != '?' || bytes[i + 2] != 'A'
					|| (bytes[i + 3] != 'V' && bytes[i + 3] != 'U')) {
					continue;
				}
				int end = i + 4;
				while (end < length && bytes[end] != 0 && end - i < 512) {
					end++;
				}
				String decorated = new String(bytes, i, end - i, StandardCharsets.US_ASCII);
				String name = readableName(decorated);
				boolean hit = exact ? name.equalsIgnoreCase(text) : name.toLowerCase().contains(needle);
				if (!hit) {
					continue;
				}
				monitor.checkCancelled();
				classes++;
				long td = base + i - 16;
				long tdRva = td - imageBase;
				emit(name + "  (" + decorated + ")  type descriptor rva 0x" + Long.toHexString(tdRva));
				boolean any = false;
				List<Long> cols = findCols(tdRva);
				for (long col : cols) {
					int offset = readInt32At(col + 4);
					List<Long> pointers = findPointers(col);
					if (pointers.isEmpty()) {
						emit("    COL rva 0x" + Long.toHexString(col - imageBase) + " (subobject offset 0x"
							+ Integer.toHexString(offset) + "): no qword in data points at it");
					}
					for (long vft : pointers) {
						any = true;
						StringBuilder line = new StringBuilder();
						line.append("    vftable rva 0x").append(Long.toHexString(vft + 8 - imageBase))
								.append("  (").append(toAddr(vft + 8)).append(")  subobject offset 0x")
								.append(Integer.toHexString(offset)).append("  COL rva 0x")
								.append(Long.toHexString(col - imageBase));
						emit(line.toString());
						for (int s = 0; s < slots; s++) {
							long slot = readInt64At(vft + 8 + 8L * s);
							emit("      [" + s + "] rva 0x" + Long.toHexString(slot - imageBase));
						}
					}
				}
				if (cols.isEmpty()) {
					emit("    no complete object locator (abstract, or only used as a base)");
				}
				i = end;
			}
		}
		emit("Classes: " + classes);
		write(out);
	}

	/** ".?AVbad_alloc@std@@" -> "std::bad_alloc"; templates (?$...) stay decorated. */
	static String readableName(String decorated) {
		String name = decorated.endsWith("@@") ? decorated.substring(4, decorated.length() - 2) : decorated.substring(4);
		if (name.startsWith("?$") || name.isEmpty()) {
			return name;
		}
		String[] parts = name.split("@");
		StringBuilder s = new StringBuilder();
		for (int p = parts.length - 1; p >= 0; p--) {
			if (!parts[p].isEmpty()) {
				s.append(s.length() > 0 ? "::" : "").append(parts[p]);
			}
		}
		return s.toString();
	}

	/** COLs (x64): signature 1, then offset, cdOffset, pTypeDescriptor rva, pClassDescriptor rva, pSelf rva. */
	private List<Long> findCols(long tdRva) {
		List<Long> cols = new ArrayList<>();
		long imageBase = currentProgram.getImageBase().getOffset();
		for (int b = 0; b < data.size(); b++) {
			byte[] bytes = data.get(b);
			long base = ranges.get(b)[0];
			int length = (int) ranges.get(b)[1];
			for (int i = (int) ((4 - (base & 3)) & 3); i + 24 <= length; i += 4) {
				if (readInt32(bytes, i) == 1 && (readInt32(bytes, i + 12) & 0xFFFFFFFFL) == tdRva
					&& (readInt32(bytes, i + 20) & 0xFFFFFFFFL) == base + i - imageBase) {
					cols.add(base + i);
				}
			}
		}
		return cols;
	}

	/** Addresses of 8-aligned qwords that hold the absolute address target (vftable[-1] = COL). */
	private List<Long> findPointers(long target) {
		List<Long> hits = new ArrayList<>();
		for (int b = 0; b < data.size(); b++) {
			byte[] bytes = data.get(b);
			long base = ranges.get(b)[0];
			int length = (int) ranges.get(b)[1];
			for (int i = (int) ((8 - (base & 7)) & 7); i + 8 <= length; i += 8) {
				if (readInt64(bytes, i) == target) {
					hits.add(base + i);
				}
			}
		}
		return hits;
	}

	private int readInt32At(long address) {
		try {
			return currentProgram.getMemory().getInt(toAddr(address));
		}
		catch (Exception e) {
			return 0;
		}
	}

	private long readInt64At(long address) {
		try {
			return currentProgram.getMemory().getLong(toAddr(address));
		}
		catch (Exception e) {
			return 0;
		}
	}

	private static int readInt32(byte[] b, int at) {
		return (b[at] & 0xFF) | (b[at + 1] & 0xFF) << 8 | (b[at + 2] & 0xFF) << 16 | (b[at + 3] & 0xFF) << 24;
	}

	private static long readInt64(byte[] b, int at) {
		return (readInt32(b, at) & 0xFFFFFFFFL) | ((long) readInt32(b, at + 4)) << 32;
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
