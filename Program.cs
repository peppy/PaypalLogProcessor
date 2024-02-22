using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CsvHelper;
using CsvHelper.Configuration;

namespace PaypalLogProcessor
{
    static class Program
    {
        private static readonly string[] ignore_types = new[]
        {
            "Payment Refund",
            //"General Currency Conversion",
            //"General Payment",
            "Account Hold for Open Authorisation",
            //"Pre-approved Payment Bill User Payment",
            "Payment Reversal",
            //"Website Payment",
            "Hold on Balance for Dispute Investigation",
            "Payment Review Hold",
            //"Subscription Payment",
            //"Express Checkout Payment",
            "Chargeback",
            //"Donation Payment",
            "Chargeback Fee",
            //"General Withdrawal",
            "Hold on Available Balance",
            //"Mass payment",
            //"Fee for Mass Pay request",
            "General Account Correction",
        };

        private static string getOutputFilename(string output) => $"out.{output}.csv";

        private const string currency_conversion_type = "General Currency Conversion";

        public static void Main(string[] args)
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("en-AU");

            List<dynamic> transactions = getTransactions();

            Console.WriteLine($"Read {transactions.Count} transactions");
            Console.WriteLine();

            Console.WriteLine("Parsing data types..");
            Parallel.ForEach(transactions, t =>
            {
                t.DateTime = DateTime.Parse(t.Date);
                t.Net = decimal.Parse(t.Net);
                t.Balance = decimal.Parse(t.Balance);

                t.NetUSD = t.Net;
            });

            Console.WriteLine("Ordering by date..");
            transactions = transactions.OrderBy(t => t.DateTime).ToList();

            Console.WriteLine($"Opening Balance: {transactions.First().Balance - transactions.First().Net}");
            Console.WriteLine($"Closing Balance: {transactions.Last().Balance}");

            outputGSTTransactions(transactions);
            outputExpenses(transactions);
        }

        private static void outputGSTTransactions(List<dynamic> transactions)
        {
            transactions = transactions
                .Where(t => t.BalanceImpact == "Credit")
                .Where(t => t.Status == "Completed" || t.Status == "Pending")
                .Where(t => t.BuyerCountryCode == "AU")
                .ToList();

            transactions = transactions.Where(t => t.Type != currency_conversion_type).ToList();

            Console.WriteLine($"Filtered to {transactions.Count()} AU transactions");

            Console.WriteLine($"Writing to disk...");
            writeOutput(transactions, getOutputFilename("gst"));

            foreach (var t in transactions)
                t.GST = t.NetUSD / 11;

            Console.WriteLine($"GST Revenue Total (USD$): {transactions.Sum(t => (decimal)t.GST):C}");

            Console.WriteLine("Monthly:");

            foreach (var typeGroup in transactions.GroupBy(t => t.DateTime.Month.ToString()))
                Console.WriteLine($"{typeGroup.Sum(t => (decimal)t.GST):C}");

            Console.WriteLine($"Done!");
        }

        private static void outputExpenses(List<dynamic> transactions)
        {
            transactions = transactions
                .Where(t => t.BalanceImpact == "Debit")
                .Where(t => t.Status == "Completed" || t.Status == "Pending")
                .Where(t => ignore_types.All(ty => ty != t.Type)).ToList();

            // remove currency conversion lines from main list.
            var conversions = transactions.Where(t => t.Type == currency_conversion_type).ToList();

            transactions = transactions.Where(t => t.Type != currency_conversion_type).ToList();

            Console.WriteLine($"Filtered to {transactions.Count()} debit transactions");

            Console.WriteLine($"Adding currency rates for {conversions.Count()} found conversions...");
            // associate currency conversions and rates
            foreach (var c in conversions)
            {
                var txn = transactions.FirstOrDefault(t => t.TransactionID == c.ReferenceTxnID);
                if (txn != null)
                {
                    txn.Rate = c.Net / txn.Net;
                    txn.NetUSD = c.Net;
                }
                else
                {
                    Console.WriteLine(
                        $"Couldn't find matching transaction for currency conversion ({c.ReferenceTxnID})");
                }
            }

            Console.WriteLine("Summary:");

            foreach (var typeGroup in transactions.GroupBy(t => t.Type))
                Console.WriteLine($"{typeGroup.Key.PadRight(50)} : {typeGroup.Sum(t => (decimal)t.NetUSD):C}");

            var output = new List<dynamic>();

            Console.WriteLine($"Creating output records...");

            foreach (var t in transactions)
            {
                dynamic o = new ExpandoObject();
                o.Date = t.Date.PadLeft(10, '0');
                o.Payee = string.IsNullOrEmpty(t.Name) ? "PayPal" : t.Name;
                o.Currency = t.Currency;
                o.Amount = $"{t.Net:F2}";
                o.Number = t.TransactionID;
                o.Notes = string.Join('\t', new[] { t.Subject, t.Note }.Where(s => !string.IsNullOrEmpty(s)));
                o.Type = t.Type;
                if (t.Currency != "USD")
                {
                    try
                    {
                        o.Rate = t.Rate;
                    }
                    catch (Exception)
                    {
                        Console.WriteLine($"Missing conversion rate for transaction {o.Number}");
                    }
                }

                output.Add(o);
            }

            Console.WriteLine($"Writing to disk...");

            writeOutput(output, getOutputFilename("expenses"));

            Console.WriteLine($"Done!");
        }

        private static void writeOutput(List<object> output, string filename)
        {
            using (var writer = File.CreateText(filename))
            using (var csv = new CsvWriter(writer, new Configuration
            {
                QuoteAllFields = true,
            }))
            {
                csv.WriteRecords(output);
            }
        }

        private static List<dynamic> getTransactions()
        {
            var transactions = new List<dynamic>();
            foreach (var f in Directory.GetFiles("./", "*.CSV", SearchOption.AllDirectories))
            {
                if (Path.GetFileName(f).StartsWith("out"))
                    continue;

                Console.WriteLine($"Reading from {f}...");
                using (var csv = new CsvReader(File.OpenText(f), new Configuration
                {
                    PrepareHeaderForMatch = s => s.Replace(" ", "")
                }))
                {
                    transactions.AddRange(csv.GetRecords<dynamic>());
                }
            }

            return transactions;
        }
    }
}