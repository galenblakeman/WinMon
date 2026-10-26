# Units in table values

## Intent

Keep metric headings short and make every numeric value readable without looking back at its heading.

## Behavior

- Processes uses CPU, GPU, Memory and I/O headings. CPU and GPU values keep `%`; Memory values choose binary units from B through TiB; I/O rates choose decimal units from B/s through TB/s. Scaled values use one decimal place. Grouped Memory still measures summed private bytes, explained by its tooltip.
- Services uses CPU, Memory and I/O headings. Memory continues to measure the private bytes of the host and included workers. Its values carry `%` and the same adaptive memory and I/O units as Processes.
- Usage already uses unit-free headings: Share values carry `%`, I/O totals use adaptive byte units, and CPU/GPU time values use hours:minutes:seconds. Those formats remain in place.

## Landed result and departures

Implemented in the Processes and Services views and a shared table formatter. I/O columns are wider to fit the unit. The Usage view already used adaptive units and needed no format change. The result follows the intent above.
