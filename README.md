# PaypalLogProcessor

This is a tool to process paypal transaction log output into a format my accounting application can handle. Probably not useful to anyone else.

- Removes all non-expense items (there are too many individual transactions to handle this way, I import using monthly summaries).
- Apply currency conversion rates to their respective transactions.