# 快速上手

目标：5~10 分钟内完成一次 **编写 → 编译 → 下发 → 运行 → 看到日志** 的完整闭环。

## 前置条件

- CoralinkerHost 已启动，浏览器能打开 Web 界面。
- 至少一个 MCU 节点已通过 USB 连接并上线（在图形界面上可见）。

## 步骤

### 1. 新建或导入逻辑

在 Web 编辑器中新建一个逻辑文件，粘贴以下代码（即 `examples/hello.cs`）：

```csharp
using CartActivator;

[LogicRunOnMCU(scanInterval = 1000)]
public class HelloWorld : LadderLogic<CartDefinition>
{
    public override void Operation(int iteration)
    {
        Console.WriteLine($"Hello DIVER! iteration={iteration}");
    }
}
```

这段代码每 1000ms 执行一次 `Operation`，打印一行日志。

### 2. 编译

点击编译按钮。编译成功后，下方终端会显示 `Build succeeded`。

如果编译失败，检查：
- 是否包含 `using CartActivator;`。
- 类是否继承了 `LadderLogic<T>`，且 `T` 继承自 `CartDefinition`。
- `Operation` 方法签名是否为 `public override void Operation(int iteration)`。

更多编译问题参见 [08-faq.md](08-faq.md)。

### 3. 下发到节点

- 在图形界面上选择目标节点。
- 为该节点指定刚刚编译好的逻辑。

### 4. 启动运行

点击启动按钮，所有已分配逻辑的节点开始运行。节点卡片上的状态会变为 `running`。

### 5. 查看日志

打开日志面板，你应该能看到类似以下输出：

```
Hello DIVER! iteration=0
Hello DIVER! iteration=1
Hello DIVER! iteration=2
...
```

每秒一行，`iteration` 递增。

## 验证成功的标准

满足以下三项即可判定"上手成功"：

1. 编译通过，无报错。
2. 节点启动后状态为 `running`。
3. 日志面板持续输出 `Hello DIVER!`。

## 下一步

- 跑 `examples/numeric.cs`，验证变量面板中 UpperIO / LowerIO 的读写。
- 读 [02-logic-api.md](02-logic-api.md)，了解全部可用 API。
