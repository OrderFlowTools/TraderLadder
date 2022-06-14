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

namespace Gemify.OrderFlow
{
    internal class LadderNotesReader
    {
        private double tickPriceIncrement;
        private string instrumentName;
        private Indicator ind = new Indicator();

        public LadderNotesReader(string instrument, double tickPriceIncrement)
        {
            this.tickPriceIncrement = tickPriceIncrement;
            this.instrumentName = instrument;
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
                    string key = values[1];
                    string note = values[2];

                    // For Convergent StalkZones, the BandPrice column determines how many prices +/- are included in zone.
                    int bandPrice = 0;
                    if (values.Length > 20)
                    {
                        int.TryParse(values[20], out bandPrice);
                    }

                    // If the entry is for another instrument, skip it
                    if (!string.Equals(instrument.Trim(), instrument,StringComparison.InvariantCultureIgnoreCase))
                    {
                        continue;
                    }

                    // If key is a range of prices (separated by -)
                    if (key.Contains("-") || bandPrice > 1)
                    {
                        double lowerBound;
                        double upperBound;

                        if (key.Contains('-'))
                        {
                            string[] priceBounds = key.Split('-');
                            if (!double.TryParse(priceBounds[0], out lowerBound)) continue;
                            if (!double.TryParse(priceBounds[1], out upperBound)) continue;
                        }
                        else
                        {
                            double price = Convert.ToDouble(key);
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
                        if (!double.TryParse(key, out price)) continue;
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