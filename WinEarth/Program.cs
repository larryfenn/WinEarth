using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Drawing;
using System.Drawing.Imaging;
using HtmlAgilityPack;
using System.Drawing.Drawing2D;

namespace WinEarth
{
    static class Program
    {
        private static string storagePath = @"C:\Users\larry\Downloads\Desktop\WinEarth";

        static void Main(string[] args)
        {
            Wallpaper[] screens = { new Wallpaper(1), new Wallpaper(2), new Wallpaper(0) }; // order comes from the monitor order in Displays
            WebClient[] clients = { new WebClient(), new WebClient(), new WebClient() };
            string[] filenames = { "left.png", "center.png", "right.png" };
            Rectangle[] crops = { new Rectangle(2600, 94, 2400, 1350), new Rectangle(0, 132, 3317, 1866), new Rectangle(0, 0, 4676, 2630) };
            List<Task> tasks = new List<Task>();
            while (true)
            {
                // string[] meso_pages = GetMesoscaleUrl(clients[0]);
                Uri[] page_urls = {
                    new Uri("https://www.star.nesdis.noaa.gov/goes/conus.php?sat=G18"),
                    new Uri("https://www.star.nesdis.noaa.gov/GOES/sector.php?sat=G16&sector=eus"),
                    // new Uri(meso_pages[0]),
                    // new Uri(meso_pages[1]),
                    new Uri("https://www.star.nesdis.noaa.gov/GOES/conus.php?sat=G16")
                };
                int[] page_item_indices = { 6, 6, 6 };
                for (int i = 0; i < 3; i++)
                {
                    tasks.Add(DownloadImageFileAsync(i, page_urls[i], page_item_indices[i], filenames[i], crops[i], screens[i], clients[i]));
                }
                try
                {
                    Task.WhenAll(tasks).Wait();
                }
                catch (Exception e)
                {
                    
                }
                Thread.Sleep(300000);
            }
        }
        private static async Task DownloadImageFileAsync(int task_id, Uri fullUrl, int index, string filename, Rectangle crop, Wallpaper screen, WebClient client)
        {
            string filePath = Path.Combine(storagePath, filename);
            bool success = false;
            int retries = 0;
            while (!success & retries < 3)
            {
                try
                {
                    string imageUrl = GetImageUrl(await client.DownloadStringTaskAsync(fullUrl), index);
                    await client.DownloadFileTaskAsync(imageUrl, filePath);
                    Crop(filePath, crop, task_id == 2); // the 2 means we're doing the rightmost monitor, which is 4K
                    success = true;
                    screen.Set(filePath);
                }
                catch (Exception e)
                {
                    retries++;
                    using (StreamWriter sw = File.AppendText(@"C:\Users\larry\corelog.txt"))
                    {
                        sw.WriteLine(DateTime.Now + "|" + e.GetType().Name + "|" + e.Message + "|" + e.StackTrace);
                    }
                }
            }
            if (!success)
            {
                using (StreamWriter sw = File.AppendText(@"C:\Users\larry\corelog.txt"))
                {
                    sw.WriteLine("Failed to update!");
                }
            }
        }
        private static string GetImageUrl(string html, int index)
        {
            HtmlDocument htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(html);
            var table = htmlDoc.DocumentNode.Descendants("div").Where(node => node.GetAttributeValue("class", "").Contains("Links")).ToList();
            return table[0].Descendants("a").ToArray<HtmlNode>()[index].Attributes["href"].Value;
        }
        private static string[] GetMesoscaleUrl(WebClient client)
        {
            HtmlDocument htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(client.DownloadString("https://www.star.nesdis.noaa.gov/GOES/meso_index.php"));
            var list = htmlDoc.DocumentNode.Descendants("div").Where(node => node.GetAttributeValue("class", "").Contains("MesoScroll")).ToList();
            string[] results = new string[2];
            results[0] = "https://www.star.nesdis.noaa.gov/GOES/" + WebUtility.HtmlDecode(list[0].Descendants("a").ToArray<HtmlNode>()[0].Attributes["href"].Value);
            results[1] = "https://www.star.nesdis.noaa.gov/GOES/" + WebUtility.HtmlDecode(list[0].Descendants("a").ToArray<HtmlNode>()[1].Attributes["href"].Value);
            return results;
        }
        public static void Crop(string filePath, Rectangle crop, bool hd_screen)
        {
            Bitmap b = new Bitmap(filePath);
            using (Bitmap scaled_bitmap = new Bitmap(hd_screen ? 3840 : 1920, hd_screen ? 2160 : 1080))
            {
                using (Bitmap cropped_bitmap = new Bitmap(crop.Width, crop.Height))
                {
                    using (Graphics g = Graphics.FromImage(cropped_bitmap))
                    {
                        g.CompositingMode = CompositingMode.SourceCopy;
                        g.CompositingQuality = CompositingQuality.HighQuality;
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.SmoothingMode = SmoothingMode.HighQuality;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        using (var wrapMode = new ImageAttributes())
                        {
                            wrapMode.SetWrapMode(WrapMode.TileFlipXY);
                            g.DrawImage(b, -crop.X, -crop.Y);
                            b.Dispose();
                        }
                    }
                    using (Graphics g = Graphics.FromImage(scaled_bitmap))
                    {
                        g.CompositingMode = CompositingMode.SourceCopy;
                        g.CompositingQuality = CompositingQuality.HighQuality;
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.SmoothingMode = SmoothingMode.HighQuality;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        using (var wrapMode = new ImageAttributes())
                        {
                            wrapMode.SetWrapMode(WrapMode.TileFlipXY);
                            g.DrawImage(cropped_bitmap, new Rectangle(0, 0, hd_screen ? 3840 : 1920, hd_screen ? 2160 : 1080), 0, 0, cropped_bitmap.Width, cropped_bitmap.Height, GraphicsUnit.Pixel, wrapMode);
                            scaled_bitmap.Save(filePath, ImageFormat.Png);
                        }
                    }
                }
            }
        }
    }
}
