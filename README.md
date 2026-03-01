# MyPsdWriter

C# から呼び出せる、シンプルな PSD 書き込みライブラリです。

この実装は Adobe の PSD 仕様（特に Header / Layer and Mask Information / Image Data）を基に、
以下をサポートする最小構成を提供します。

- 複数レイヤーの書き込み
- 各レイヤーの透明情報（Alpha）
- 透明背景上に複数の透明領域を持つレイヤー生成
- 合成済みプレビュー（Composite image）書き込み
- グローバル画像データは RGB 3ch で出力します（Photoshop で余計なアルファマスク表示を防止）。

参照: https://www.adobe.com/devnet-apps/photoshop/fileformatashtml/

## ライブラリの使い方

```csharp
using MyPsdWriter;

var doc = new PsdDocument
{
    Width = 512,
    Height = 512,
    Compression = CompressionMethod.Rle // デフォルトは互換性の高い RLE
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

## サンプル: PNG 3枚を順番にレイヤー化して PSD 出力

`samples/PngLayersToPsdSample` は PNG ファイル 3 つを読み込み、
指定順のままレイヤーとして PSD に書き出すサンプルです。
PNG は内部で RGBA32 に正規化され、各画像サイズのままレイヤー化されます。

```bash
dotnet run --project samples/PngLayersToPsdSample -- \
  ./layer01.png ./layer02.png ./layer03.png ./output.psd
```

- `layer01.png` が最下層、`layer03.png` が最上層として追加されます。
- 3枚の PNG は同じサイズでなくても動作します（キャンバスは最大サイズ）。
- 各レイヤー名はファイル名（拡張子なし）になります。

## 注意

- 現在は 8-bit RGB + Alpha 前提です。
- 読み込み機能は含みません（書き込み専用）。
- チャンネル圧縮は `CompressionMethod.Raw`, `Rle`, `ZipWithoutPrediction`, `ZipWithPrediction` の4種類を実装しています。
  - デフォルトは `Rle` です（Photoshop, Affinity, CLIP STUDIO PAINT で互換確認済み）。
  - `Zip` 系はファイルサイズが非常に小さくなりますが、サードパーティ製ツール（CLIP STUDIO PAINT 等）が読み込み非対応で透明になる問題があるため注意が必要です。
