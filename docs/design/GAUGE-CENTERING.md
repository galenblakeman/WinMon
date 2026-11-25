# Resource gauge centering

Full-size resource tiles used horizontal margins of 10 and 8 DIPs. Their content was
therefore centered one DIP to the right of the containing column, although the bowl
and value were aligned with each other. The existing gauges smoke correctly rejected
the offset using its half-DIP tolerance. The same margins were used in compact tiles,
where the one-DIP separator changes the relation to the outer column center.

Use symmetric 9-DIP horizontal margins in both layouts. This retains the 18-DIP total
inset and available content width while removing the asymmetric content offset. The
compact separator remains unchanged. Gauge geometry, values, resource sampling,
saved preferences and diagnostic tolerance retain their existing behavior.

The regression matrix covers both views, 1000/836-DIP widths, Dark/Light, optional second
disk and all three arc styles. The formerly failing workflow must reach cancellation
before writing captures rather than expecting a centering exception. Gauge and compact
smokes cover all styles and the compact renders include 100/150/200-percent scales.

Landed in MainWindow.xaml and MainWindow.Compact.cs, with regression coverage in
DiagnosticGaugeWorkflowCharacterizationTests.cs. The implementation follows this
scope without changes to the gauge renderer or tolerance.
