using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SunEngine.Commons.Utils;
using SunEngine.Options;
using Path = System.IO.Path;
using PointF = SixLabors.Primitives.PointF;

namespace SunEngine.Services
{
    public class CaptchaService
    {
        public const string CryptserviceName = "Captcha";
        
        private readonly TimeSpan cacheTimeout = new TimeSpan(0, 3, 0);

        private readonly Font font;


        private readonly CryptService cryptService;

        
        public CaptchaService(IOptions<CaptchaOptions> captchaOptions, CryptService cryptService)
        {
            this.cryptService = cryptService;

            // Init Font (font name: Gunny Rewritten)
            FontCollection fontCollection = new FontCollection();
            fontCollection.Install(Path.GetFullPath("gunnyrewritten.ttf"));
            font = fontCollection.Families.First().CreateFont(46);
        }

        public string MakeCryptedCaptchaToken()
        {
            var token = new CaptchaToken
            {
                Text = GenerateCaptchaText(),
                Expire = DateTime.UtcNow.Add(cacheTimeout),
                Guid = Guid.NewGuid()
            };

            var tokenJson = JsonConvert.SerializeObject(token);
            return cryptService.Crypt(CryptserviceName,tokenJson);
        }


        string GenerateCaptchaText()
        {
            Random ran = new Random();
            string text = ran.Next(10000, 999999).ToString();
            return text;
        }

        public string GetTextFromToken(string token)
        {
            string json = cryptService.Decrypt(CryptserviceName,token);
            return JsonConvert.DeserializeObject<CaptchaToken>(json).Text;
        }

        public bool VerifyToken(string token, string text)
        {
            string json = cryptService.Decrypt(CryptserviceName,token);
            CaptchaToken captchaToken = JsonConvert.DeserializeObject<CaptchaToken>(json);
            if (captchaToken.Expire < DateTime.UtcNow)
                return false;

            return string.Equals(captchaToken.Text, text);
        }

        public MemoryStream MakeCaptchaImage(string text)
        {
            RendererOptions ro = new RendererOptions(font)
            {
                VerticalAlignment = VerticalAlignment.Center,
                TabWidth = 10
            };

            var rect = TextMeasurer.MeasureBounds(text, ro);

            MemoryStream ms;
            using (Image<Rgba32> img = new Image<Rgba32>((int) rect.Width + 10, (int) rect.Height + 6))
            {
                var textGraphicsOptions =
                    new TextGraphicsOptions(true)
                    {
                        VerticalAlignment = VerticalAlignment.Center,
                        TabWidth = 10
                    };

                PointF[] points = {new PointF(2, img.Height / 2), new PointF(img.Width - 2, img.Height / 2)};
                
                img.Mutate(ctx => ctx
                    .Fill(Rgba32.FromHex("f0f4c3")) // white background image
                    .DrawLines(Rgba32.Black, 3, points)
                    .DrawText(textGraphicsOptions, text, font, Rgba32.Black, new PointF(0, img.Height / 2)));

                ms = new MemoryStream();

                img.Save(ms, new JpegEncoder());
            }

            ms.Seek(0, SeekOrigin.Begin);
            return ms;
        }

        public class CaptchaToken
        {
            public string Text { get; set; }
            public DateTime Expire { get; set; }
            public Guid Guid { get; set; }
        }
    }
}