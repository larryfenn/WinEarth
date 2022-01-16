using System.IO;
using System.Net;

namespace WinEarth
{
    class Program
    {
        static void Main(string[] args)
        {
            Wallpaper[] screens = { new Wallpaper(2), new Wallpaper(0), new Wallpaper(1) }; // order comes from the monitor order in Displays

            string[] urls =
            {
                "https://cdn.star.nesdis.noaa.gov/GOES16/ABI/SECTOR/eus/GEOCOLOR/20220160441_GOES16-ABI-eus-GEOCOLOR-2000x2000.jpg",
                "https://cdn.star.nesdis.noaa.gov/GOES16/ABI/CONUS/GEOCOLOR/20220160441_GOES16-ABI-CONUS-GEOCOLOR-2500x1500.jpg",
                "https://cdn.star.nesdis.noaa.gov/GOES16/ABI/SECTOR/ne/GEOCOLOR/20220160441_GOES16-ABI-ne-GEOCOLOR-2400x2400.jpg"
            };
            string[] filenames = { "left.png", "center.png", "right.png"};
            string storagePath = @"C:\Users\larry\Downloads\Desktop\EarthView";

            using (var client = new WebClient())
            {
                for (int i = 0; i < 3; i++)
                {
                    string storageFilename = filenames[i];
                    string picFilename = Path.Combine(storagePath, storageFilename);
                    client.DownloadFile(urls[i], picFilename); // TODO: handle HTTP errors
                    screens[i].Set(picFilename);
                }
            }
        }
    }
}
