using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace AgitCon {
    // OS-local OCR of an imported file only. No screen capture or UI access.
    static class LocalOcr {
        public static async Task<string> Read(string path,CancellationToken cancel){
            var engine=OcrEngine.TryCreateFromLanguage(new Language("ko-KR"));
            if(engine==null)return "";
            byte[] bytes;
            using(var input=System.Drawing.Image.FromFile(path)){
                if(input.Width>4096||input.Height>4096)throw new InvalidDataException("OCR size limit");
                double scale=Math.Min(4.0,1024.0/Math.Max(input.Width,input.Height));
                using(var surface=new Bitmap(Math.Max(1,(int)(input.Width*scale)),Math.Max(1,(int)(input.Height*scale)))){
                    using(var graphics=Graphics.FromImage(surface)){graphics.Clear(Color.White);graphics.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;graphics.DrawImage(input,new Rectangle(Point.Empty,surface.Size));}
                    using(var output=new MemoryStream()){surface.Save(output,ImageFormat.Png);bytes=output.ToArray();}
                }
            }
            using(var stream=new InMemoryRandomAccessStream()){
                using(var writer=new DataWriter(stream.GetOutputStreamAt(0))){writer.WriteBytes(bytes);await writer.StoreAsync().AsTask(cancel);await writer.FlushAsync().AsTask(cancel);writer.DetachStream();}
                stream.Seek(0);var decoder=await BitmapDecoder.CreateAsync(stream).AsTask(cancel);
                using(var bitmap=await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Premultiplied).AsTask(cancel)){
                    var result=await engine.RecognizeAsync(bitmap).AsTask(cancel);cancel.ThrowIfCancellationRequested();return String.Join("\n",result.Lines.Select(line=>line.Text)).Trim();
                }
            }
        }
    }
}
