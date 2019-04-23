using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
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
            "General Withdrawal",
            "Hold on Available Balance",
            //"Mass payment",
            //"Fee for Mass Pay request",
            "General Account Correction",
        };

        private const string output_filename = "out.csv";

        private const string currency_conversion_type = "General Currency Conversion";

        public static void Main(string[] args)
        {
            List<dynamic> transactions = getTransactions();

            Console.WriteLine($"Read {transactions.Count} transactions");

            transactions = transactions
                .Where(t => t.BalanceImpact == "Debit")
                .Where(t => t.Status == "Completed")
                .Where(t => ignore_types.All(ty => ty != t.Type)).ToList();

            // remove currency converison lines from main list.
            var conversions = transactions.Where(t => t.Type == currency_conversion_type).ToList();
            
            transactions = transactions.Where(t => t.Type != currency_conversion_type).ToList();
            
            Console.WriteLine($"Filtered to {transactions.Count()} debit transactions");
            
            Console.WriteLine($"Adding currency rates for {conversions.Count()} found conversions...");
            // associate currency conversions and rates
            foreach (var c in conversions)
            {
                var txn = transactions.FirstOrDefault(t => t.TransactionID == c.ReferenceTxnID);
                if (txn != null)
                    txn.Rate = decimal.Parse(c.Net) / decimal.Parse(txn.Net);
            }

            var output = new List<dynamic>();
            
            Console.WriteLine($"Creating output records...");

            foreach (var t in transactions)
            {
                dynamic o = new ExpandoObject();
                o.Date = t.Date.PadLeft(10, '0');
                o.Payee = string.IsNullOrEmpty(t.Name) ? "PayPal" : t.Name;
                o.Currency = t.Currency;
                o.Amount = t.Net;
                o.Number = t.TransactionID;
                o.Notes = string.Join('\t', new [] { t.Subject, t.Note }.Where(s => !string.IsNullOrEmpty(s)));
                if (t.Currency != "USD")
                    o.Rate = t.Rate;

                output.Add(o);
            }
            
            Console.WriteLine($"Writing to disk...");

            writeOutput(output, output_filename);
            
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
                if (Path.GetFileName(f) == output_filename)
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