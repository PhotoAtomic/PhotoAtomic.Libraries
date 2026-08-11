using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Nyota.Helpers.IndentedInterpolatedStringHandler;

namespace Nyota.Tests;
public class StringIndentTests
{
	[Test]
	public async Task StringIndentTestAsync()
	{
		string text = Indent($$"""
			a a a a
				{{GenerateC()}}
			b b b b 
			""");
		await Assert.That(text).IsEqualTo("""
			a a a a
				C C C
				D D D
			b b b b 
			""");
	}

	private static string GenerateC()
	{
		return Indent($$""""
			C C C
			D D D
			"""");
	}

	[Test]
	public async Task SplitterEnumTestAsync()
	{
		var splitter = "abc\ndefg xxx\nUUU".AsSpan().SplitAfter('\n');
		splitter.MoveNext();
		var part1 = splitter.Current.ToString();
		splitter.MoveNext();
		var part2 = splitter.Current.ToString();
		splitter.MoveNext();
		var part3 = splitter.Current.ToString();

		await Assert.That(splitter.MoveNext()).IsFalse();

		await Assert.That(part1).IsEqualTo("abc\n");
		await Assert.That(part2).IsEqualTo("defg xxx\n");
		await Assert.That(part3).IsEqualTo("UUU");
	}


	[Test]
	public async Task ComplexCodeLikeIndentTestAsync()
	{
		var cases = Indent($$""""
			case "a":
				//do something
				break;
			case "b":
				//do other
				break;
			"""");

		var sw = Indent($$""""
			switch (text){
				{{cases}}
				default:
					break;
			}
			"""");

		var ifBody = Indent($$""""
			foreach(var c in text){
				//for body
				if(c == 'a'){
					//this is the body for a case
					//another line
				}
				//other line
			}
			"""");

		var fun = Indent($$""""
			public void Compute(string inputValue){
				var text = inputValue.Trim();
				{{sw}}
				if(text is not null){
					{{ifBody}}
				}
			}
			"""");





		string res = fun;


		var expected = """"
			public void Compute(string inputValue){
				var text = inputValue.Trim();
				switch (text){
					case "a":
						//do something
						break;
					case "b":
						//do other
						break;
					default:
						break;
				}
				if(text is not null){
					foreach(var c in text){
						//for body
						if(c == 'a'){
							//this is the body for a case
							//another line
						}
						//other line
					}
				}
			}
			"""";

		await Assert.That(res).IsEqualTo(expected);
	}


	[Test]
	public async Task SupressLineWithNullTestAsync()
	{
		string? cases = null;

		var sw = Indent($$""""
			switch (text){
				{{cases}}
				default:
					break;
			}
			"""");

		var expected = """"
			switch (text){
				default:
					break;
			}
			"""";

		string res = sw;
		await Assert.That(res).IsEqualTo(expected);

	}

	[Test]
	public async Task LeaveEmptyLineWithNullTestAsync()
	{		

		var sw = Indent($$""""
			switch (text){
				default:
					{{string.Empty}}
					break;
			}
			"""");

		var expected = """"
			switch (text){
				default:
					
					break;
			}
			"""";

		string res = sw;
		await Assert.That(res).IsEqualTo(expected);

	}

	[Test]
	public async Task ConcatenateMultipleIndentInStringBuilderAsync()
	{
		string[] choices = ["a", "b"];
		var caseBuilder = new StringBuilder();
		foreach (var choice in choices)
		{
			caseBuilder.Append(Indent($$"""
				case "{{choice}}":
					{{choice}} = "{{choice}}";
					break;

				"""));
		}

		var text =  caseBuilder.ToString();


		string switchCode = Indent($$"""
			switch(val){
				{{text}}
				default:
					//do something
					break;
			}			
			""");

		var expected = """"
			switch(val){
				case "a":
					a = "a";
					break;
				case "b":
					b = "b";
					break;
				default:
					//do something
					break;
			}			
			"""";
		
		await Assert.That(switchCode.ToString()).IsEqualTo(expected);
	}

	[Test]
	public async Task SimpleStringLiteralAsync()
	{
		string? pre = null;
		pre = "pre ";
		var combined = Indent($$""""{{pre}}value another"""");

		await Assert.That(combined.ToString()).IsEqualTo("pre value another");
	}
}
