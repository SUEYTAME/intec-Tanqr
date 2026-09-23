// Lector de QR: BarcodeDetector nativo (Chrome Android) o, si no existe o no lee QR,
// el polyfill `barcode-detector` (ZXing en WebAssembly). El .wasm se sirve desde la
// propia aplicación, nunca desde un CDN.
type Detected = { rawValue: string };
export type Detector = {
  detect(source: ImageBitmapSource | HTMLVideoElement): Promise<Detected[]>;
};
type NativeConstructor = {
  new (options: { formats: string[] }): Detector;
  getSupportedFormats(): Promise<string[]>;
};

let pending: Promise<Detector> | null = null;

async function create(): Promise<Detector> {
  const native = (window as unknown as { BarcodeDetector?: NativeConstructor })
    .BarcodeDetector;
  if (native && (await native.getSupportedFormats()).includes("qr_code"))
    return new native({ formats: ["qr_code"] });
  const [{ BarcodeDetector, prepareZXingModule }, { default: wasmUrl }] =
    await Promise.all([
      import("barcode-detector/ponyfill"),
      import("zxing-wasm/reader/zxing_reader.wasm?url"),
    ]);
  prepareZXingModule({
    overrides: {
      locateFile: (path: string, prefix: string) =>
        path.endsWith(".wasm") ? wasmUrl : prefix + path,
    },
  });
  return new BarcodeDetector({ formats: ["qr_code"] });
}

export function getDetector() {
  pending ??= create().catch((error: unknown) => {
    pending = null;
    throw error;
  });
  return pending;
}

// Primer QR legible de una imagen (foto o captura).
export async function decodeImage(file: Blob) {
  const detector = await getDetector();
  const bitmap = await createImageBitmap(file);
  try {
    const found = await detector.detect(bitmap);
    return found[0]?.rawValue ?? null;
  } finally {
    bitmap.close();
  }
}
