# PKHeX 上游 API 适配 - ImageUtil / SpriteUtil (.NET 10)

## 概述

PKHeX 上游在 .NET 10 升级过程中，对 `ImageUtil` 和相关图像处理 API 进行了重构，
将原有的静态方法改为 C# 14 扩展方法（`extension(Bitmap bmp)`），并移除了部分旧 API。
本次修复共解决 **8 个编译错误**，涉及 `RaidCrawler.WinForms` 项目中的 2 个文件。

## 错误清单

| # | 文件 | 行号 | 错误码 | 描述 |
|---|------|------|--------|------|
| 1 | `RewardsView.cs` | 45 | CS1503 | `ImageUtil.LayerImage` 第一个参数从 `Image` 改为 `Bitmap` |
| 2 | `MainWindow.cs` | 1018 | CS0117 | `ImageUtil.GetPixelData` 方法已移除 |
| 3 | `MainWindow.cs` | 1038 | CS1503 | `SpriteUtil.GetSpriteGlow` 第一个参数从 `Image` 改为 `Bitmap` |
| 4 | `MainWindow.cs` | 1049 | CS1503 | 同上 |
| 5 | `MainWindow.cs` | 1356 | CS1503 | `ImageUtil.BlendTransparentTo` 从静态方法改为 `Bitmap` 扩展方法 |
| 6 | `MainWindow.cs` | 1364 | CS1503 | 同上 |
| 7 | `MainWindow.cs` | 1367 | CS1503 | 同上 |
| 8 | `MainWindow.cs` | 1402 | CS1503 | `ImageUtil.LayerImage` 第一个参数从 `Image` 改为 `Bitmap` |

## PKHeX 上游 API 变更详情

### 1. `ImageUtil.GetPixelData` → `Bitmap.GetBitmapData()`

**旧 API（已移除）：**
```csharp
byte[] pixels = ImageUtil.GetPixelData(bitmap);
```

**新 API（C# 14 扩展方法）：**
```csharp
byte[] pixels = bitmap.GetBitmapData();
```

`GetBitmapData()` 是在 `extension(Bitmap bmp)` 块中定义的扩展方法，返回 `byte[]`，
内部通过 `LockBits` / `UnlockBits` 获取像素数据。

### 2. `ImageUtil.BlendTransparentTo` 从静态方法变为扩展方法

**旧 API（返回新 Image）：**
```csharp
Image result = ImageUtil.BlendTransparentTo(img, color, opacity, start);
```

**新 API（就地修改 Bitmap，void 返回）：**
```csharp
((Bitmap)img).BlendTransparentTo(color, opacity, start);
// img 已被就地修改
```

### 3. `ImageUtil.LayerImage` 签名变更

**旧签名：**
```csharp
public static Bitmap LayerImage(Image baseLayer, Image overLayer, int x, int y)
public static Bitmap LayerImage(Image baseLayer, Image overLayer, int x, int y, double transparency)
```

**新签名：**
```csharp
public static Bitmap LayerImage(Bitmap baseLayer, Image overLayer, int x, int y)
public static Bitmap LayerImage(Bitmap baseLayer, Bitmap overLayer, int x, int y, double transparency)
```

第一个参数 `baseLayer` 从 `Image` 收窄为 `Bitmap`；
带透明度的重载中 `overLayer` 也收窄为 `Bitmap`。

### 4. `SpriteUtil.GetSpriteGlow` 参数类型

参数类型从 `Image` 改为 `Bitmap`（与 `GetBitmapData` 扩展方法配合）。

## 修复方案

### 文件: `MainWindow.cs`

1. **地图字段类型** (`MapBase`, `MapKitakami`, `MapBlueberry`)：
   从 `Image` 改为 `Bitmap`，通过显式类型转换 `(Bitmap)Image.FromStream(...)` 实现。
   这同时修复了 `LayerImage(map, gem, ...)` 调用（错误 #8）。

2. **`GetDisplayGemImage` 方法**：
   - 将 `baseImg` 的类型从 `(Image?)` 改为 `(Bitmap?)`，确保下游调用类型匹配。
   - `GetPixelData` → `GetBitmapData()`（错误 #2）。
   - `GetSpriteGlow` 调用添加 `(Bitmap)` 显式转换（错误 #3, #4）。

3. **`ApplyColor` 方法**：
   - 在方法入口将 `img` 转换为 `Bitmap bmp`。
   - 将 `ImageUtil.BlendTransparentTo(img, ...)` 静态调用改为 `bmp.BlendTransparentTo(...)` 扩展方法调用。
   - 由于扩展方法是 void 返回，改为先调用方法再 `return bmp`（错误 #5, #6, #7）。

### 文件: `RewardsView.cs`

1. **`GetItem` 返回值**：
   `GetItem` 返回 `Image?`，在传给 `LayerImage` 时添加 `(Bitmap)` 转换（错误 #1）。

## 注意事项

- `Image.FromStream()` 对 PNG/BMP 格式实际返回的运行时类型就是 `Bitmap`，因此显式转换是安全的。
- `ResourceManager.GetObject()` 对图像资源返回的运行时类型也是 `Bitmap`。
- 剩余 2 个 **SYSLIB1224 警告**属于 `RaidCrawler.Core` 项目中的 `JsonSerializable` 用法问题，
  与本次修复无关，不影响运行。
