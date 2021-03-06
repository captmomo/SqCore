using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.ServiceModel.Syndication;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Xml.Linq;
using SqCommon;
using Newtonsoft.Json;
using Microsoft.AspNetCore.SignalR;

namespace SqCoreWeb
{
    public enum NewsSource
    {
        YahooRSS,
        CnbcRss,
        Benzinga,
        TipRanks
    }
    public class NewsItem
    {
        public string Ticker { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string LinkUrl { get; set; } = string.Empty;
        public DateTime DownloadTime { get; set; }
        public DateTime PublishDate { get; set; }
        public string Source { get; set; } = string.Empty;
        public string DisplayText { get; set; } = string.Empty;
        public string Sentiment { get; set; } = string.Empty;
    }

    public class QuickfolioNewsDownloader
    {
        Dictionary<int, List<NewsItem>> m_newsMemory = new Dictionary<int, List<NewsItem>>();
        Random m_random = new Random(DateTime.Now.Millisecond);
        KeyValuePair<int, int> m_sleepBetweenDnsMs = new KeyValuePair<int, int>(2000, 1000);     // <fix, random>
        string[] m_stockTickers = { "AAPL", "ADBE", "AMZN", "BABA", "CRM", "FB", "GOOGL", "MA", "MSFT", "NVDA", "PYPL", "QCOM", "V" };

        public QuickfolioNewsDownloader()
        {
            UpdateStockTickers();
        }

        public void UpdateStockTickers()
        {
            string valuesFromGSheetStr = "Error. Make sure GoogleApiKeyKey, GoogleApiKeyKey is in SQLab.WebServer.SQLab.NoGitHub.json !";
            if (!String.IsNullOrEmpty(Utils.Configuration["Google:GoogleApiKeyName"]) && !String.IsNullOrEmpty(Utils.Configuration["Google:GoogleApiKeyKey"]))
            {
                if (!Utils.DownloadStringWithRetry(out valuesFromGSheetStr, "https://sheets.googleapis.com/v4/spreadsheets/1c5ER22sXDEVzW3uKthclpArlZvYuZd6xUffXhs6rRsM/values/A1%3AA1?key=" + Utils.Configuration["Google:GoogleApiKeyKey"]))
                    valuesFromGSheetStr = "Error in DownloadStringWithRetry().";
            }
            if (!valuesFromGSheetStr.StartsWith("Error"))
            {
                ExtractTickers(valuesFromGSheetStr);
            }
        }

        private void ExtractTickers(string p_spreadsheetString)
        {
            int pos = p_spreadsheetString.IndexOf(@"""values"":");
            if (pos < 0)
                return;
            p_spreadsheetString = p_spreadsheetString.Substring(pos + 9); // cut off until the end of "values":
            int posStart = p_spreadsheetString.IndexOf(@"""");
            if (posStart < 0)
                return;
            int posEnd = p_spreadsheetString.IndexOf(@"""", posStart + 1);
            if (posEnd < 0)
                return;
            string cellValue = p_spreadsheetString.Substring(posStart + 1, posEnd - posStart - 1);
            m_stockTickers = cellValue.Split(',').Select(x => x.Trim()).ToArray();
        }

        internal List<NewsItem> GetCommonNews()
        {
            string rssFeedUrl = string.Format(@"https://www.cnbc.com/id/100003114/device/rss/rss.html");

            List<NewsItem> foundNewsItems = new List<NewsItem>();
            // try max 5 downloads to leave the tread for sure (call this method repeats continuosly)
            int retryCount = 0;
            while ((foundNewsItems.Count < 1) && (retryCount < 5))
            {
                foundNewsItems = ReadRSS(rssFeedUrl, NewsSource.CnbcRss, "");
                if (foundNewsItems.Count == 0)
                    System.Threading.Thread.Sleep(m_sleepBetweenDnsMs.Key + m_random.Next(m_sleepBetweenDnsMs.Value));
                retryCount++;
            }
            // AddFoundNews(0, foundNewsItems);
            // return NewsToString(m_newsMemory[0]);
            return foundNewsItems;
        }

        internal List<string> GetStockTickers()
        {
            return new List<string> { "All assets" }.Union(m_stockTickers).ToList();
        }

        internal void GetStockNews(IClientProxy? p_clients)
        {
            foreach (string ticker in m_stockTickers)
            {
                string rssFeedUrl = string.Format(@"https://feeds.finance.yahoo.com/rss/2.0/headline?s={0}&region=US&lang=en-US", ticker);
                p_clients.SendAsync("quickfNewsStockNewsUpdated", ReadRSS(rssFeedUrl, NewsSource.YahooRSS, ticker));
                p_clients.SendAsync("quickfNewsStockNewsUpdated", ReadBenzingaNews(ticker));
                p_clients.SendAsync("quickfNewsStockNewsUpdated", ReadTipranksNews(ticker));
            }
        }

        private List<NewsItem> ReadTipranksNews(string p_ticker)
        {
            List<NewsItem> foundNewsItems = new List<NewsItem>();
            if (foundNewsItems == null)
                foundNewsItems = new List<NewsItem>();
            //MakeRequests();
            string url = string.Format(@"https://www.tipranks.com/api/stocks/getNews/?ticker={0}", p_ticker);
            string webpageData;
            HttpStatusCode status = GetPageData(url, out webpageData);
            if (status == HttpStatusCode.OK)
            {
                ReadTipranksNews(foundNewsItems, p_ticker, webpageData);
            }
            return foundNewsItems;
        }
        private void ReadTipranksNews(List<NewsItem> p_foundNewsItems, string p_ticker, string webpageData)
        {
            try
            {
                Newtonsoft.Json.Linq.JObject json = Newtonsoft.Json.Linq.JObject.Parse(webpageData);

                if (json == null)
                    return;
                if (!json.HasValues)
                    return;
                var jsonNews = json.First;
                if (jsonNews == null)
                    return;
                var jsonNewsList = jsonNews.First;
                if (jsonNewsList == null)
                    return;
                var jsonNewsItem = jsonNewsList.First;
                while (jsonNewsItem != null)
                {
                    if (jsonNewsItem == null)
                        return;
                    NewsItem newsItem = new NewsItem();
                    newsItem.Ticker = p_ticker;
                    Newtonsoft.Json.Linq.JToken? token = jsonNewsItem.SelectToken("url");
                    if (token == null)
                        continue;
                    newsItem.LinkUrl = jsonNewsItem.Value<string>("url");
                    token = jsonNewsItem.SelectToken("title");
                    if (token == null)
                        continue;
                    newsItem.Title = WebUtility.HtmlDecode(jsonNewsItem.Value<string>("title"));
                    newsItem.Summary = "  ";
                    token = jsonNewsItem.SelectToken("sentiment");
                    if (token != null)
                    {
                        newsItem.Sentiment = jsonNewsItem.Value<string>("sentiment");
                    }
                    DateTime date;
                    token = jsonNewsItem.SelectToken("articleTimestamp");
                    if (token == null)
                        continue;
                    if (DateTime.TryParse(jsonNewsItem.Value<string>("articleTimestamp"), out date))
                        newsItem.PublishDate = date;
                    newsItem.DownloadTime = DateTime.Now;
                    newsItem.Source = NewsSource.TipRanks.ToString();

                    p_foundNewsItems.Add(newsItem);
                    jsonNewsItem = jsonNewsItem.Next;
                }
            }
            catch (Exception)
            {
                DateTime.Today.AddDays(1);
            }
        }
        private List<NewsItem> ReadBenzingaNews(string p_ticker)
        {
            List<NewsItem> foundNewsItems = new List<NewsItem>();
            if (foundNewsItems == null)
                foundNewsItems = new List<NewsItem>();
            string url = string.Format(@"https://www.benzinga.com/stock/{0}", p_ticker);
            string webpageData;
            HttpStatusCode status = GetPageData(url, out webpageData);
            System.Threading.Thread.Sleep(m_sleepBetweenDnsMs.Key + m_random.Next(m_sleepBetweenDnsMs.Value));
            if (status == HttpStatusCode.OK)
            {
                ReadBenzingaSection(foundNewsItems, p_ticker, webpageData, "headlines");
                ReadBenzingaSection(foundNewsItems, p_ticker, webpageData, "press");
            }
            return foundNewsItems;
        }

        public static HttpStatusCode GetPageData(string p_uri, out string p_pageData)
        {
            HttpStatusCode status = (HttpStatusCode)0;
            HttpWebResponse resp = new HttpWebResponse();

            // initialize the out param (in case of error)
            p_pageData = "";

            ServicePointManager.Expect100Continue = true;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            try
            {
                // create the web request
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(p_uri);

                // disable the proxy?
                //if (this.m_noProxy)
                //{
                request.Proxy = new WebProxy();
                //    req.ProtocolVersion = HttpVersion.Version10; // default is 1.1
                //}
                // make the connection
                resp = (HttpWebResponse)request.GetResponse();

                // get the page data
                StreamReader sr = new StreamReader(resp.GetResponseStream());
                p_pageData = sr.ReadToEnd();
                sr.Close();

                // get the status code (should be 200)
                status = resp.StatusCode;
            }

            catch (WebException e)
            {
                string str = e.Status.ToString();

                resp = (HttpWebResponse)e.Response;
                if (null != resp)
                {
                    // get the failure code from the response
                    status = resp.StatusCode;
                    str += status;
                }
                else
                {
                    status = (HttpStatusCode)(-1);  // generic connection error
                }
            }
            catch
            {
                status = (HttpStatusCode)(-2);
            }
            finally
            {
                // close the response
                if (resp != null)
                {
                    resp.Close();
                }
            }
            return status;
        }

        private void ReadBenzingaSection(List<NewsItem> p_foundNewsItems, string p_ticker, string p_webpageData, string p_keyWord)
        {
            Regex regexBenzingaLists = new Regex(@"<div[^>]*?class=""stories""[^>]*?" + p_keyWord + @"(?<CONTENT>(\s|\S)*?)</div>"
                , RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
            Regex regexBenzingaNews = new Regex(@"<li(\s|\S)*?class=""story""(\s|\S)*?<a href=""(?<LINK>[^""]*)"">(?<TITLE>[^<]*)<(\s|\S)*?<span class=""date"">(?<DATE>[^<]*)"
                , RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
            MatchCollection matches = regexBenzingaLists.Matches(p_webpageData);
            if (matches == null)
                return;
            for (int index = 0; index < matches.Count;index++)
            {
                Match match = matches[index];
                MatchCollection matchesNews = regexBenzingaNews.Matches(match.Groups["CONTENT"].Value);
                for (int indexNews= 0; indexNews < matchesNews.Count; indexNews++)
                {
                    Match matchNews = matchesNews[indexNews];
                    NewsItem newsItem = new NewsItem();
                    newsItem.Ticker = p_ticker;
                    newsItem.LinkUrl = matchNews.Groups["LINK"].Value;
                    newsItem.Title = WebUtility.HtmlDecode(matchNews.Groups["TITLE"].Value);
                    newsItem.Summary = "  ";
                    newsItem.PublishDate = GetNewsDate(matchNews.Groups["DATE"].Value);
                    newsItem.DownloadTime = DateTime.Now;
                    newsItem.Source = NewsSource.Benzinga.ToString();

                    p_foundNewsItems.Add(newsItem);
                }
            }
        }
        private DateTime GetNewsDate(string p_dateString)
        {
            DateTime date;
            if (DateTime.TryParse(p_dateString, out date))
                return date;
            p_dateString = p_dateString.ToUpper();
            if (p_dateString.Contains("AGO"))
            {
                p_dateString = p_dateString.Replace("AGO", "").Trim();
                if (p_dateString.Contains("HOUR"))
                {
                    p_dateString = p_dateString.Replace("HOURS", "").Replace("HOUR", "").Trim();
                    int hours;
                    if (int.TryParse(p_dateString, out hours))
                        return DateTime.Now.AddHours(-hours);
                }
                else if (p_dateString.Contains("DAY"))
                {
                    p_dateString = p_dateString.Replace("DAYS", "").Replace("DAY", "").Trim();
                    int days;
                    if (int.TryParse(p_dateString, out days))
                        return DateTime.Now.AddDays(-days);
                }
                else if (p_dateString.Contains("MIN"))
                {
                    p_dateString = p_dateString.Replace("MINUTES", "").Replace("MIN", "").Replace("MINS", "").Trim();
                    int days;
                    if (int.TryParse(p_dateString, out days))
                        return DateTime.Now.AddDays(-days);
                }
            }
            return DateTime.Now;
        }
        private string NewsToString(List<NewsItem> newsList)
        {
            string finalString = string.Empty;
            foreach (NewsItem news in newsList.OrderBy(x => x.PublishDate))
                finalString += string.Format(@"news_ticker{0}news_title{1}news_summary{2}news_link{3}news_downloadTime{4:yyyy-MM-dd hh:mm}news_publishDate{5:yyyy-MM-dd hh:mm}news_source{6}news_end",
                    news.Ticker, news.Title, news.Summary, news.LinkUrl, news.DownloadTime, news.PublishDate, news.Source);
            return finalString;
        }


        private void AddFoundNews(int p_stockID, List<NewsItem> p_foundNewsItems)
        {
            List<NewsItem> notYetKnownNews = new List<NewsItem>();
            if (!m_newsMemory.ContainsKey(p_stockID))
                m_newsMemory.Add(p_stockID, new List<NewsItem>());
            foreach (NewsItem newsItem in p_foundNewsItems)
                if (m_newsMemory[p_stockID].Where(x => x.LinkUrl.Equals(newsItem.LinkUrl)).Count() == 0)    // not yet known
                {
                    m_newsMemory[p_stockID].Add(newsItem);
                    notYetKnownNews.Add(newsItem);
                }
        }

        private static List<NewsItem> ReadRSS(string p_url, NewsSource p_newsSource, string p_ticker)
        {
            try
            {
                var webClient = new WebClient();
                // hide ;-)
                webClient.Headers.Add("user-agent", "Mozilla/4.0 (compatible; MSIE 6.0; Windows NT 5.2; .NET CLR 1.0.3705;)");
                // fetch feed as string
                var content = webClient.OpenRead(p_url);
                var contentReader = new StreamReader(content);
                var rssFeedAsString = contentReader.ReadToEnd();
                // convert feed to XML using LINQ to XML and finally create new XmlReader object
                var feed = System.ServiceModel.Syndication.SyndicationFeed.Load(XDocument.Parse(rssFeedAsString).CreateReader());

                List<NewsItem> foundNews = new List<NewsItem>();

                foreach (SyndicationItem item in feed.Items)
                {
                    NewsItem newsItem = new NewsItem();
                    newsItem.Ticker = p_ticker;
                    newsItem.LinkUrl = item.Links[0].Uri.AbsoluteUri;
                    newsItem.Title = WebUtility.HtmlDecode(item.Title.Text);
                    newsItem.Summary = WebUtility.HtmlDecode(item.Summary.Text);
                    newsItem.PublishDate = item.PublishDate.LocalDateTime;
                    newsItem.DownloadTime = DateTime.Now;
                    newsItem.Source = p_newsSource.ToString();
                    newsItem.DisplayText = string.Empty;
                    //newsItem.setFiltered();

                    foundNews.Add(newsItem);
                }
                return foundNews;
            }
            catch (Exception exception)
            {
                Console.WriteLine(exception.Message);
                return new List<NewsItem>();
            }
        }
    }
}