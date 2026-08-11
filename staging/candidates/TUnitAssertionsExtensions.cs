using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TUnit.Assertions.AssertionBuilders;

namespace Nyota.TestsExtensions;
public static class TUnitAssertionsExtensions
{


	public static void Check<T>(this InvokableValueAssertionBuilder<T> assertionBuilder)
	{
		ArgumentNullException.ThrowIfNull(assertionBuilder);
		var _ = assertionBuilder.GetAssertionResults().Result;
	}
}
