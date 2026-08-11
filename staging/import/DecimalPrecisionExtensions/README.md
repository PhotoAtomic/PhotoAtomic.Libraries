*Project Description*
{project:description}
*DecimalPrecision* adds extension methods to work with the decimal data type.
Decimal are extremely useful when the size of number handled are relatively small and the decimal digits are very important and must not change.
Typical scenario are scientific measurement in validated environments or monetary operations, where the implicit rounding introduced by the floating points type are not acceptable.

In these contests is often important to know the precision of the measured value, or in other words, the number of decimal places after the decimal separator.

Usually data types like float or double cannot handle insignificant zeroes in number like 3.140
But these zeroes are not always insignificant. In certain context it is important to know the precision of the machinery that have produced the value and the fact that that third decimal digit was effectively read as Zero, because this means that the original value is at worst 3.14049999...
Otherwise, considering 3.14 we could also suppose that the original value can also be 3.1449999.. that makes a great difference in certain scenarios.

So, insignificant zeroes matters.

System.Decimal can handle this situation but lacks of functions that helps with this job.

Usually developer does this +wrong+ by manipulating strings and then parsing these again.
This is a bad practice because strings representations depends on system culture and can lead to disastrous results if precautions are not taken. Plus, it is very ugly and unstylish to manipulate strings... and sounds like a practice of the previous century.

*DecimalPrecision* works directly on the bits that makes the representation of the decimal type. 
System.Decimal can by itself represent insignificant zeroes, but usually these are removed during the calculation and no functions are available in the framework to programmatically work on the precision of the number.

The library is very light, it has no external dependencies other than frameworks assembly. it is compiled for the 4.0 but works also on the 3.5 or the 4.5

the main function is _SetPrecision_ that is exposed as an extension method of the decimal type.

you can for example write

_(123.4m).SetPrecision(3); // this create a decimal that is 123.400_

other functions helps more with rounding instead of truncation.

Tests are available for a quick reference.
