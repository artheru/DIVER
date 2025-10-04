import subprocess
import sys
import shutil
import re
import json
import os
import pathlib

# Function to check if the tool exists


def check_tools():
    global arm_embedded_toolchain_prefix

    if shutil.which('./arm_ram_overlay.ld') is None:
        print(f'Error: The linker script file is missing')
        sys.exit(1)

    script_path = os.path.abspath(__file__)
    script_dir = os.path.dirname(script_path)
    arm_embedded_toolchain_dir = os.path.join(
        script_dir, 'arm_embedded_toolchain')
    arm_gcc_path = os.path.join(
        arm_embedded_toolchain_dir, 'bin', 'arm-none-eabi-gcc')
    print(arm_gcc_path)
    if os.path.exists(arm_gcc_path) or os.path.exists(arm_gcc_path+'.exe'):
        arm_embedded_toolchain_prefix = os.path.join(
            arm_embedded_toolchain_dir, 'bin', 'arm-none-eabi-')
        print(
            f'Local arm embedded toolchain found, in {arm_embedded_toolchain_prefix}')
    else:
        print(f'Local arm embedded toolchain is missing')
        print(f'Searching in PATH')
        arm_gcc_path = shutil.which('arm-none-eabi-gcc')
        if arm_gcc_path is None:
            print(f'Error: The arm embedded toolchain are missing')
            print(
                f'visit https://developer.arm.com/downloads/-/arm-gnu-toolchain-downloads to download')
            sys.exit(1)
        else:
            arm_embedded_toolchain_prefix = 'arm-none-eabi-'
            print(f'Global arm embedded toolchain found, in {arm_gcc_path}')

    print('All required tools / files are available.')


def compile_dll(c_files, output_dll):
    if shutil.which('gcc') is None:
        print(f'Warn: The gcc is missing, skipping dll')
        return

    try:
        # 构建编译DLL命令
        compile_dll_command = [
            'gcc', '-o', output_dll,
            '-O3', '-Os', '-fPIC', '-shared'
        ] + c_files

        # 编译DLL
        subprocess.run(compile_dll_command, check=True)
        print(f'Compiled dll to {output_dll} successfully.')
    except subprocess.CalledProcessError as e:
        print(f'Error during generating dll: {e}')
        sys.exit(1)


def compile_and_link(c_files, output_elf):
    global arm_embedded_toolchain_prefix

    try:
        # 构建编译命令 (Build compilation command)
        # Generate position-independent code for address-relative function calls
        # Add -fPIC for position independent code
        # Add -nostartfiles to avoid standard library initialization
        compile_command = [
            arm_embedded_toolchain_prefix + 'gcc',
            '-o', output_elf,
            '-g', '-O3', '-Os',
            '-fPIC',  # Position Independent Code for relocatable functions
            '-ffreestanding', '-nostdlib', '-nodefaultlibs', '-nostartfiles',
            '-mcpu=cortex-m4', '-mthumb', '-mfloat-abi=hard', '-mfpu=fpv4-sp-d16',
            '-Tarm_ram_overlay.ld',
            '-lm',  # Link math library for math.h functions
        ] + c_files

        # 编译和链接
        subprocess.run(compile_command, check=True)
        print(f'Compiled and linked to {output_elf} successfully.')
    except subprocess.CalledProcessError as e:
        print(f'Error during compilation/linking: {e}')
        sys.exit(1)


def generate_bin(elf_file, output_bin):
    global arm_embedded_toolchain_prefix

    try:
        # 使用arm-none-eabi-objcopy生成bin文件
        objcopy_command = [arm_embedded_toolchain_prefix + 'objcopy',
                           '-O', 'binary',
                           '-j', '.text',
                           '-j', '.rodata',
                           '-j', '.bss',
                           '-j', '.data',
                           '--set-section-flags', '.bss=alloc,load,contents',
                           elf_file, output_bin]
        subprocess.run(objcopy_command, check=True)
        print(f'Generated BIN file: {output_bin}')
    except subprocess.CalledProcessError as e:
        print(f'Error during BIN generation: {e}')
        sys.exit(1)


def binary_to_hex_text(input_file, output_file):
    try:
        # Open the binary file in read-binary mode
        with open(input_file, 'rb') as bin_file:
            # Read all bytes from the file
            data = bin_file.read()

        # Open the output text file in write mode
        with open(output_file, 'w') as txt_file:
            # Write the opening brace with a new line
            txt_file.write('{\n')

            # Define the number of bytes per line
            bytes_per_line = 16

            # Process the data in chunks of bytes_per_line
            for i in range(0, len(data), bytes_per_line):
                # Extract the chunk of bytes
                chunk = data[i:i + bytes_per_line]
                # Convert each byte to a hex format and join with a comma and space
                hex_data = ', '.join(f'0x{byte:02X}' for byte in chunk)
                # Write the formatted hex data followed by a comma and a new line
                txt_file.write(f'  {hex_data},\n')

            # Write the closing brace, remove the last comma, and add a new line
            # Move back 2 bytes to remove the last comma and newline
            txt_file.seek(txt_file.tell() - 2, 0)
            txt_file.write('\n}\n')

        print(f'Conversion complete. Output written to {output_file}')

    except IOError as e:
        print(f'Error: {e}')
        sys.exit(1)


def generate_disassembly(elf_file, output_text):
    global arm_embedded_toolchain_prefix

    # 生成反汇编代码
    try:
        disasm_command = [arm_embedded_toolchain_prefix + 'objdump',
                          '-SlD', '-m', 'arm', elf_file]
        with open(output_text, 'w') as dis_file:
            subprocess.run(disasm_command, stdout=dis_file, check=True)
        print('Disassembly written to output.dis')
    except subprocess.CalledProcessError as e:
        print(f'Error during disassembly: {e}')
        sys.exit(1)


def extract_functions(elf_file):
    global arm_embedded_toolchain_prefix

    try:
        # 使用readelf命令提取符号表
        result = subprocess.run(
            [arm_embedded_toolchain_prefix + 'readelf', '-s', elf_file], stdout=subprocess.PIPE, text=True)
        output = result.stdout

        # 正则表达式匹配函数地址和名称
        pattern = re.compile(
            r'^\s*\d+:\s*([0-9a-fA-F]+)\s+\d+\s+FUNC\s+\w+\s+\w+\s+\w+\s+(\S+)$')

        functions = []
        for line in output.splitlines():
            match = pattern.match(line)
            if match:
                address = int(match.group(1), 16)  # 将地址转换为整数
                function_name = match.group(2)
                # Skip standard library functions and internal symbols
                if not function_name.startswith('_') and function_name in ['cfun0', 'cfun1', 'cfun2']:
                    functions.append(
                        {'name': function_name, 'entry_point': address})

        return {'functions': functions, 'alignment': 8}

    except Exception as e:
        print(f'Error: {e}')
        sys.exit(1)


def main():
    if len(sys.argv) < 3:
        print(
            f'Usage: python3 {sys.argv[0]} <dist_prefix> <source1.c> [<source2.c> ...]')
        sys.exit(1)

    check_tools()

    dist_prefix = sys.argv[1]
    if '.' in dist_prefix:
        print(f'dist_prefix can not contain "."')
        sys.exit(1)
    source_files = sys.argv[2:]
    output_elf = dist_prefix + '.elf'
    output_bin = dist_prefix + '.bin'
    output_dis = dist_prefix + '.dis'
    output_dll = dist_prefix + '.dll'
    output_json = dist_prefix + '.json'

    # 编译和链接生成ELF文件
    compile_and_link(source_files, output_elf)

    # 生成BIN文件
    generate_bin(output_elf, output_bin)

    # 生成BIN的数组形式
    binary_to_hex_text(output_bin, output_bin + '.txt')

    # 提取函数地址并输出为JSON格式
    functions_json = extract_functions(output_elf)

    # 输出到终端或保存为文件
    json_output = json.dumps(functions_json, indent=4)
    print('Functions in JSON format:')
    print(json_output)
    with open(output_json, 'w') as json_file:
        json_file.write(json_output)

    # 反汇编
    generate_disassembly(output_elf, output_dis)

    # 编译DLL
    compile_dll(source_files, output_dll)


if __name__ == '__main__':
    main()
