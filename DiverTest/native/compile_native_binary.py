import json
import os
import re
import shutil
import subprocess
import sys

def log(msg):
    print(f"[ccoder-msvc] {msg}")

def find_vcvars64():
    vswhere = r"C:\\Program Files (x86)\\Microsoft Visual Studio\\Installer\\vswhere.exe"
    if not os.path.exists(vswhere):
        raise FileNotFoundError("vswhere.exe not found")
    cmd = [vswhere, "-latest", "-products", "*", "-requires", "Microsoft.VisualStudio.Component.VC.Tools.x86.x64", "-property", "installationPath"]
    result = subprocess.run(cmd, capture_output=True, text=True, check=True)
    installation = result.stdout.strip()
    if not installation:
        raise RuntimeError("Visual Studio installation with VC tools not found")
    vcvars = os.path.join(installation, "VC", "Auxiliary", "Build", "vcvars64.bat")
    if not os.path.exists(vcvars):
        raise FileNotFoundError(f"vcvars64.bat not found at {vcvars}")
    return vcvars

def compile_with_cl(vcvars, sources, dll_path, obj_path):
    quoted_sources = " ".join(f'"{src}"' for src in sources)
    cmd = f'call "{vcvars}" && cl /nologo /LD /O2 /EHsc /MD {quoted_sources} /Fe"{dll_path}" /Fo"{obj_path}"'
    result = subprocess.run(cmd, shell=True, capture_output=True, text=True)
    if result.returncode != 0:
        raise RuntimeError(f"cl.exe failed:\n{result.stdout}\n{result.stderr}")
    if result.stdout:
        log(result.stdout.strip())
    if result.stderr:
        log(result.stderr.strip())


def extract_functions(c_files):
    primary_pattern = re.compile(r"\bNATIVE_API\s+[^\s]+\s+(cfun\d+)\s*\(")
    fallback_pattern = re.compile(r"\bcfun(\d+)\b")
    names = []
    for path in c_files:
        with open(path, "r", encoding="utf-8") as f:
            text = f.read()
        found = False
        for match in primary_pattern.finditer(text):
            func = match.group(1)
            if func not in names:
                names.append(func)
            found = True
        if not found:
            for match in fallback_pattern.finditer(text):
                func = f"cfun{match.group(1)}"
                if func not in names:
                    names.append(func)
    return names

def main():
    if len(sys.argv) < 3:
        print(f"Usage: python {sys.argv[0]} <dist_prefix> <source1.c> [<source2.c> ...]")
        sys.exit(1)
    dist_prefix = os.path.abspath(sys.argv[1])
    sources = [os.path.abspath(src) for src in sys.argv[2:]]
    workdir = os.path.dirname(dist_prefix)
    if not os.path.isdir(workdir):
        os.makedirs(workdir, exist_ok=True)

    vcvars = find_vcvars64()
    dll_path = dist_prefix + ".dll"
    obj_path = dist_prefix + ".obj"
    bin_path = dist_prefix + ".bin"
    json_path = dist_prefix + ".json"

    for path in [dll_path, obj_path, bin_path, json_path]:
        if os.path.exists(path):
            os.remove(path)

    compile_with_cl(vcvars, sources, dll_path, obj_path)

    if not os.path.exists(dll_path):
        raise FileNotFoundError(f"Expected output DLL not found at {dll_path}")

    shutil.copyfile(dll_path, bin_path)

    functions = extract_functions(sources)
    manifest = {
        "type": "pe-dll",
        "machine": "x64",
        "alignment": 16,
        "functions": functions,
    }
    with open(json_path, "w", encoding="utf-8") as f:
        json.dump(manifest, f, indent=2)
        f.write("\n")
    log(f"Generated {dll_path}")
    log(f"Generated {bin_path}")
    log(f"Generated {json_path}")

if __name__ == "__main__":
    try:
        main()
    except Exception as ex:
        log(str(ex))
        sys.exit(1)
