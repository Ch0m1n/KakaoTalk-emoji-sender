# Local embedding components

- Model: [onnx-community/embeddinggemma-2-ONNX](https://huggingface.co/onnx-community/embeddinggemma-2-ONNX), revision `daa72c51243991dfcaf9f9137d2c573d8f7790c0`, text and vision q4. Its model card is retained in `dist/semantic/gemma2/MODEL-CARD.md`; Apache 2.0.
- [Transformers.js](https://github.com/huggingface/transformers.js), 4.3.1; Apache 2.0.
- [ONNX Runtime Web](https://github.com/microsoft/onnxruntime), `1.31.0-dev.20260914-8d85527a0`; MIT.
- [WebView2 SDK](https://www.nuget.org/packages/Microsoft.Web.WebView2/1.0.4314.35), 1.0.4314.35. The NuGet package and included license are retained in `dist/semantic/gemma2/`. The Windows installation provides WebView2 Runtime.

Reference behavior was inspected in [discord-dccon](https://github.com/80ROkWOC4j/discord-dccon/blob/main/src/core/embedding.ts). The native host, offline transport, installer, indexing and application integration are separately implemented here. No Discord plugin code is embedded in the application.
