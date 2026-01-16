# 构建 / 运行 / 烧录指南

本工程实现了 **MCU ⇄ PC 的高速串行通信桥接系统**，包含嵌入式固件与 Windows 上位机程序。

---

## 一、通用构建环境（所有模块必需）

### 1. Python

* 用于：

  * SCons 构建系统
  * MCU 构建脚本
  * 资源生成 / 工具脚本
* **要求版本：Python ≥ 3.9**

验证：

```sh
python --version
pip --version
```

---

### 2. SCons

* 本工程的统一构建入口
* 用于构建：

  * MCU 固件
  * PC 端 C / C++ / C# 程序
  * 烧录与调试辅助命令

安装：

```sh
pip install scons
```

验证：

```sh
scons --version
```

---

## 二、MCU 固件构建与烧录（STM32F4）

### 1. ARM 交叉编译器

* 工具链：**arm-none-eabi-gcc**
* 用于编译 STM32F4 固件

下载地址（官方）：
[https://developer.arm.com/downloads/-/gnu-rm](https://developer.arm.com/downloads/-/gnu-rm)

安装要求：

* 将 `<install>/bin` 加入系统 `PATH`
* 验证：

```sh
arm-none-eabi-gcc --version
```

---

### 2. 安装 pyOCD

本工程使用 **pyOCD** 作为 MCU 烧录与调试工具。

安装：

```sh
pip install pyocd
```

验证：

```sh
pyocd --version
```

---

### 3. 下载 STM32F4 支持包（非常重要）

pyOCD 默认不包含 STM32F4 的目标描述文件，需要手动下载：

```sh
pyocd pack install stm32f4
```

验证是否安装成功：

```sh
pyocd list --targets | findstr STM32F4
```

---

### 4. 连接调试器

支持的调试器包括：

* ST-Link
* DAP-Link
* CMSIS-DAP

要求：

* USB 正常识别
* 板卡供电稳定
* 调试接口与 MCU 匹配

---

### 5. 编译 MCU 固件

在 **项目根目录** 执行：

```sh
scons
```

MCU 编译输出位于：

```
mcu/build/
```

常见产物：

* `firmware.elf`
* `firmware.bin`
* `firmware.hex`

---

### 6. 烧录固件到 MCU

```sh
scons flash
```

说明：

* 内部通过 pyOCD 调用调试器
* 自动下载最新构建的固件

---

### 7. 查看 MCU 运行日志（RTT）

```sh
scons rtt
```

用途：

* 实时查看 MCU printf / log 输出
* 调试串口协议、状态机、异常

---

## 三、PC 端构建环境（Windows）

### 1. 安装 Microsoft Visual Studio

推荐版本：

* **Visual Studio 2022**

需要勾选以下工作负载：

✅ 使用 C++ 的桌面开发
✅ .NET 桌面开发

必须组件：

* MSVC v143 编译器
* Windows 10 / 11 SDK
* C++/CLI 支持
* .NET SDK

---

### 2. 构建 PC 端模块

在项目根目录执行：

```sh
scons c_core
```

将构建：

* `c_core`（C 协议核心库）

然后构建 wrapper（已迁移为 csproj）：

```sh
dotnet build .\wrapper\MCUSerialBridgeWrapper.csproj
dotnet build .\wrapper\TestCS.csproj
dotnet build .\wrapper\TestDIVER.csproj
```

---

### 3. 运行 C# 测试程序

```sh
.\build\TestCS.exe
```

或手动运行生成的 EXE（`build/` 目录）。

运行前请确认：

* MCU 已烧录并运行
* 串口号正确（如 `COM5`）
* 波特率与 MCU 一致（如 `1000000`）

---

## 四、DIVER 开发快捷命令 (dev.bat)

针对 DIVER 模式开发，提供了 `dev.bat` 一键脚本：

```sh
dev.bat build   # 编译 MCU 固件
dev.bat flash   # 烧录固件到 MCU
dev.bat rtt     # 启动 RTT 日志查看器
dev.bat test    # 运行 PC 端 TestDIVER 测试程序
dev.bat all     # 编译 + 烧录 + 测试（一条龙）
```

配置项位于 `dev.bat` 文件顶部：

| 变量 | 说明 | 默认值 |
|------|------|--------|
| `PDN` | 板子型号 | `FRLD-DIVERBK-V2` |
| `TESTDIVER_PORT` | 串口号 | `COM18` |
| `TESTDIVER_BAUD` | 波特率 | `1000000` |
| `TESTDIVER_BIN` | DIVER 程序 bin 路径 | `...\TestLogic.bin` |

---

## 五、常见构建组合

### 仅构建 MCU 固件

```sh
scons mcu
```

### 仅构建 C Core

```sh
scons c_core
```

### 仅运行 C# 测试程序

```sh
.\build\TestCS.exe
```

---

## 七、清理构建产物

```sh
scons -c
```

将清理：

* MCU 编译输出
* PC 端中间文件
* 生成的 DLL / EXE

---

## 八、运行前检查清单

* [ ] Python ≥ 3.9
* [ ] SCons 已安装
* [ ] arm-none-eabi-gcc 在 PATH 中
* [ ] pyOCD 已安装
* [ ] STM32F4 pack 已下载
* [ ] 调试器连接正常
* [ ] Visual Studio + MSVC + .NET 环境完整
* [ ] 串口号与波特率确认无误

---
