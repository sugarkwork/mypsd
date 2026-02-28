# MyPsdWriter

C# から呼び出せる、シンプルな PSD 書き込みライブラリです。

この実装は Adobe の PSD 仕様（特に Header / Layer and Mask Information / Image Data）を基に、
以下をサポートする最小構成を提供します。

- 複数レイヤーの書き込み
- 各レイヤーの透明情報（Alpha）
- 透明背景上に複数の透明領域を持つレイヤー生成
- 合成済みプレビュー（Composite image）書き込み

参照: https://www.adobe.com/devnet-apps/photoshop/fileformatashtml/

## 使い方

```csharp
using MyPsdWriter;

var doc = new PsdDocument
{
    Width = 512,
    Height = 512
};

// 透明背景の上に、複数の領域だけ色を塗るレイヤー
var layer = PsdLayerFactory.CreateFromRegions(
    name: "regions",
    left: 0,
    top: 0,
    width: 512,
    height: 512,
    regions:
    [
        new TransparentRegion(20, 20, 120, 80, 255, 0, 0, 180),
        new TransparentRegion(220, 100, 160, 120, 0, 255, 0, 200),
        new TransparentRegion(120, 280, 200, 100, 0, 120, 255, 140),
    ]);

doc.Layers.Add(layer);

await using var fs = File.Create("sample.psd");
PsdWriter.Write(doc, fs);
```

## 注意

- 現在は 8-bit RGB + Alpha 前提です。
- チャンネル圧縮は RAW（無圧縮）のみです。
- 読み込み機能は含みません（書き込み専用）。
