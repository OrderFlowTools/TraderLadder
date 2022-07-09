using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading.Tasks;
using NinjaTrader.NinjaScript.Indicators;
using System.Collections.Concurrent;
using NinjaTrader.Cbi;
using System.Net;
using System.Text.RegularExpressions;

namespace Gemify.OrderFlow
{
    internal class LadderNotesReader
    {
        private double tickPriceIncrement;
        private string instrumentShortName;
        private string instrumentFullName;
        private Indicator ind = new Indicator();

        public LadderNotesReader(string instrumentShortName, string instrumentFullName, double tickPriceIncrement)
        {
            this.tickPriceIncrement = tickPriceIncrement;
            this.instrumentShortName = instrumentShortName;
            this.instrumentFullName = instrumentFullName;
        }

        internal ConcurrentDictionary<double, string> ReadCSVNotes(string csvURL)
        {
            ConcurrentDictionary<double, string> notesMap = new ConcurrentDictionary<double, string>();
            using (StringReader reader = new StringReader(ReadCSVFromURL(csvURL)))
            {
                string line = string.Empty;
                while (line != null)
                {
                    line = reader.ReadLine();
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    // CSV format expected is:
                    // INSTRUMENT,PRICE,NOTE
                    string[] values = line.Split(',');
                    string instrument = values[0];
                    string priceKey = values[1];
                    string note = values[2];

                    // If the entry is for another instrument, skip it
                    if (!string.Equals(instrument.Trim(), instrumentShortName, StringComparison.InvariantCultureIgnoreCase) &&
                        !string.Equals(instrument.Trim(), instrumentFullName, StringComparison.InvariantCultureIgnoreCase))
                    {
                        continue;
                    }

                    // For CT-StalkZones, the BandPrice column determines how many prices +/- are included in zone.
                    int bandPrice = 0;
                    if (values.Length > 20)
                    {
                        int.TryParse(values[20], out bandPrice);
                    }

                    // For CT-CloudLevels, the bandprice is denoted by xxt (xx is band, and t for ticks)
                    Regex pattern = new Regex(@"^(\d+)t\s+(.*)");
                    Match match = pattern.Match(note);
                    if (match.Success)
                    {
                        bandPrice = Convert.ToInt32(match.Groups[1].Value) / 2;
                        note = match.Groups[2].Value;
                    }

                    // If key is a range of prices (separated by -)
                            if (priceKey.Contains("-") || bandPrice > 1)
                    {
                        double lowerBound;
                        double upperBound;

                        if (priceKey.Contains('-'))
                        {
                            string[] priceBounds = priceKey.Split('-');
                            if (!double.TryParse(priceBounds[0], out lowerBound)) continue;
                            if (!double.TryParse(priceBounds[1], out upperBound)) continue;
                        }
                        else
                        {
                            double price = Convert.ToDouble(priceKey);
                            lowerBound = price - (bandPrice * tickPriceIncrement);
                            upperBound = price + (bandPrice * tickPriceIncrement);
                        }

                        double current = lowerBound;
                        while (current <= upperBound)
                        {
                            notesMap.AddOrUpdate(current, note, (k, v) => (v + ", " + note));

                            current += tickPriceIncrement;
                        }
                    }
                    else
                    {
                        double price;
                        if (!double.TryParse(priceKey, out price)) continue;
                        notesMap.AddOrUpdate(price, note, (k, v) => (v + ", " + note));
                    }
                }
            }

            return notesMap;
        }

        internal string ReadCSVFromURL(string url)
        {
            HttpWebRequest webRequest = (HttpWebRequest)WebRequest.Create(url);
            HttpWebResponse response = (HttpWebResponse)webRequest.GetResponse();

            StreamReader reader = null;
            string csv = string.Empty;
            try
            {
                reader = new StreamReader(response.GetResponseStream());
                csv = reader.ReadToEnd();
            }
            finally
            {
                if (reader != null) reader.Close();
            }

            return csv;
        }
    }
}