# Third-party notices

- MokiDown: MIT, Copyright (c) 2026 hhyufan. Theme tokens, the app icon, and “昼夜双生的星光书页” background are copied from the sibling MokiDown project under its repository license. The original license is retained in `LICENSE`.
- miaogu-notepad: interaction reference for the header and tab bar. No React runtime is embedded in this project.
- Markdig 1.5.1: BSD-2-Clause. https://github.com/xoofx/markdig
- WpfMath 2.1.0 (XAML-Math): MIT. https://github.com/ForNeVeR/xaml-math — LaTeX typesetting for WPF. The package bundles the Computer Modern fonts (`cmex10`, `cmmi10`, `cmr10`, `cmsy10`, `cmtt10`, Knuth license) and `jlm_msam10.ttf` from JLaTeXMath (SIL Open Font License); their notices ship in the NuGet package (`FONT-LICENSES.md`).
- SharpVectors 1.8.6 (`SharpVectors.Wpf`): BSD-3-Clause, Copyright (c) 2010-2026 Elinam LLC. https://github.com/ElinamLLC/SharpVectors — SVG parsing and WPF vector rendering.
- SkiaSharp 3.119.4: MIT, Copyright Xamarin, Inc. and Microsoft Corporation. https://github.com/mono/SkiaSharp — native GIF decoding. Skia/native dependency notices ship in `Assets/Licenses/`.
- Poppins and JetBrains Mono: SIL Open Font License 1.1. The same outlines as MokiDown are packaged as WPF-compatible static OpenType faces; their copyright and licenses ship in `Assets/Fonts/`.
- Moki CJK Fallback: the small Source Han Sans subset bundled by MokiDown, repackaged for WPF with a distinct family name. SIL Open Font License 1.1; see `Assets/Fonts/CJK-OFL.txt`. Chinese glyphs absent from that subset use the Windows Microsoft YaHei UI fallback, as in MokiDown.

NuGet dependencies retain their upstream licenses in their package metadata. Distribution should retain the licenses of included dependencies.
