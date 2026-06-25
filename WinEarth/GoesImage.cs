using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace WinEarth
{
    /// <summary>
    /// Shared helper for scraping a NOAA GOES page and fetching the full-resolution
    /// image it links to. Used both by the background updater and the config GUI's
    /// crop preview so the two agree on how a page/item-index resolves to an image.
    /// </summary>
    public static class GoesImage
    {
        // HttpClient is thread-safe and meant to be reused for the life of the process.
        private static readonly HttpClient httpClient = new HttpClient();

        /// <summary>
        /// NOAA's running list of available mesoscale views. A single page carries both
        /// the GOES-East and GOES-West tabs, so the same URL serves either satellite.
        /// </summary>
        public const string MesoIndexUrl = "https://www.star.nesdis.noaa.gov/goes/meso_index.php?sat=G19";

        /// <summary>
        /// Resolves the page URL of the top (most-used) mesoscale view currently being
        /// run by NOAA. The list page carries both tabs in document order — GOES-East
        /// first, then GOES-West — so <paramref name="west"/> selects between them.
        /// The returned absolute URL points at a <c>meso.php</c> sector page whose
        /// "Links" list can then be scraped with <see cref="ExtractImageUrl"/> like any
        /// other GOES page. There is no fixed URL per view: callers must re-resolve each
        /// update cycle, because the available views change as products start and stop.
        /// </summary>
        public static async Task<string> ResolveTopMesoscalePageAsync(bool west)
        {
            string html = await httpClient.GetStringAsync(MesoIndexUrl);
            return ExtractTopMesoscaleUrl(html, west);
        }

        /// <summary>
        /// Picks the top anchor from the requested tab's scroll list and resolves it to
        /// an absolute URL. Split out from <see cref="ResolveTopMesoscalePageAsync"/> so
        /// the parsing can be exercised without a network round-trip.
        /// </summary>
        public static string ExtractTopMesoscaleUrl(string html, bool west)
        {
            HtmlDocument htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(html);

            // Each tab renders its available views inside a "MesoScroll" div; they appear
            // in document order as [0] GOES-East, [1] GOES-West.
            var scrolls = htmlDoc.DocumentNode.Descendants("div")
                .Where(node => node.GetAttributeValue("class", "").Contains("MesoScroll"))
                .ToList();
            int tab = west ? 1 : 0;
            if (scrolls.Count <= tab)
            {
                throw new InvalidOperationException(
                    "The mesoscale list is missing the requested GOES-East/West tab.");
            }

            HtmlNode[] anchors = scrolls[tab].Descendants("a").ToArray();
            if (anchors.Length == 0)
            {
                throw new InvalidOperationException(
                    "No mesoscale views are currently available for the requested satellite.");
            }

            // The list is sorted descending by use, so the first anchor is the one we want.
            // Hrefs are page-relative and may contain HTML-encoded ampersands.
            string href = WebUtility.HtmlDecode(anchors[0].Attributes["href"].Value);
            return new Uri(new Uri(MesoIndexUrl), href).ToString();
        }

        /// <summary>
        /// Extracts the href of the <paramref name="index"/>-th anchor in the page's
        /// "Links" list. Higher indices generally correspond to higher resolutions.
        /// </summary>
        public static string ExtractImageUrl(string html, int index)
        {
            HtmlDocument htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(html);
            var table = htmlDoc.DocumentNode.Descendants("div")
                .Where(node => node.GetAttributeValue("class", "").Contains("Links"))
                .ToList();
            if (table.Count == 0)
            {
                throw new InvalidOperationException("No 'Links' section found on the page.");
            }

            HtmlNode[] anchors = table[0].Descendants("a").ToArray();
            if (index < 0 || index >= anchors.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(index),
                    string.Format("Item index {0} is out of range; the page has {1} link(s).", index, anchors.Length));
            }

            return anchors[index].Attributes["href"].Value;
        }

        /// <summary>
        /// Scrapes <paramref name="pageUrl"/>, resolves the item-index link (relative
        /// hrefs are resolved against the page), downloads it, and returns it as a
        /// fresh <see cref="Bitmap"/>. The caller owns the returned bitmap.
        /// </summary>
        public static async Task<Bitmap> DownloadAsync(string pageUrl, int index)
        {
            string html = await httpClient.GetStringAsync(pageUrl);
            string href = ExtractImageUrl(html, index);

            // Pages may use absolute or page-relative hrefs; Uri resolution handles both.
            Uri imageUri = new Uri(new Uri(pageUrl), href);

            using (HttpResponseMessage response = await httpClient.GetAsync(imageUri))
            {
                response.EnsureSuccessStatusCode();
                using (Stream stream = await response.Content.ReadAsStreamAsync())
                using (Image source = Image.FromStream(stream))
                {
                    // Copy off the response stream so the bitmap owns its own pixels.
                    return new Bitmap(source);
                }
            }
        }
    }
}
