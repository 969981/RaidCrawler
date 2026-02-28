# PKHeX 上游 API 适配记录

## 背景
在更新 PKHeX 上游核心库 (`PKHeX.Core`) 后，`RaidCrawler` 插件在编译时遇到了找不到 `BatchEditing` 符号的错误。

## 报错信息
```
Cannot resolve symbol 'BatchEditing'
```
报错位置：`RaidCrawler.Core\Structures\RaidFilter.cs` 的 `IsBatchFilterSatisfied` 方法内。

## 变更分析
原先的 PKHeX.Core 包含名为 `BatchEditing` 的类，其中封装了用于批量过滤 (Batch Filter) 的逻辑。
最新的 PKHeX.Core 进行了一次较大的重构，将批量编辑相关的逻辑拆分并放入了 `PKHeX.Core\Editing\Bulk` 命名空间下。

- `BatchEditing.ScreenStrings` 方法的逻辑被转移到了 `EntityBatchEditor` 这个新类中，变为静态方法 `EntityBatchEditor.ScreenStrings`。
- `BatchEditing.IsFilterMatch` 方法现在属于 `EntityBatchEditor` 实例方法，需要通过 `EntityBatchEditor.Instance.IsFilterMatch` 调用，其本质是将 PKM 对象与一组 `StringInstruction` 进行匹配检查。

## 修复方案
在 `RaidCrawler.Core\Structures\RaidFilter.cs` 文件中进行如下代码替换：

**修改前：**
```csharp
BatchEditing.ScreenStrings(filters);
return BatchEditing.IsFilterMatch(filters, blank);
```

**修改后：**
```csharp
EntityBatchEditor.ScreenStrings(filters);
return EntityBatchEditor.Instance.IsFilterMatch(filters, blank);
```

## 相关提交
相关的修改已应用到代码库中。确保在继续开发前项目可以正常编译通过。
