using System.Text;
using static PhotoAtomic.IndentedStrings.IndentedInterpolatedStringHandler;

namespace PhotoAtomic.IndentedStrings.Tests;
public class StringIndentTests
{
	[Fact]
	public void StringIndentTest()
	{
		string text = Indent($$"""
			a a a a
				{{GenerateC()}}
			b b b b 
			""");
		Assert.Equal("""
			a a a a
				C C C
				D D D
			b b b b 
			""", text);
	}

	private static string GenerateC()
	{
		return Indent($$""""
			C C C
			D D D
			"""");
	}

	[Fact]
	public void SplitterEnumTest()
	{
		var splitter = "abc\ndefg xxx\nUUU".AsSpan().SplitAfter('\n');
		splitter.MoveNext();
		var part1 = splitter.Current.ToString();
		splitter.MoveNext();
		var part2 = splitter.Current.ToString();
		splitter.MoveNext();
		var part3 = splitter.Current.ToString();

		Assert.False(splitter.MoveNext());

		Assert.Equal("abc\n", part1);
		Assert.Equal("defg xxx\n", part2);
		Assert.Equal("UUU", part3);
	}


	[Fact]
	public void ComplexCodeLikeIndentTest()
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

		Assert.Equal(expected, res);
	}


	[Fact]
	public void SupressLineWithNullTest()
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
		Assert.Equal(expected, res);

	}

	[Fact]
	public void LeaveEmptyLineWithNullTest()
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
		Assert.Equal(expected, res);

	}

	[Fact]
	public void ConcatenateMultipleIndentInStringBuilder()
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
		
		Assert.Equal(expected, switchCode.ToString());
	}

	[Fact]
	public void SimpleStringLiteral()
	{
		string? pre = null;
		pre = "pre ";
		var combined = Indent($$""""{{pre}}value another"""");

		Assert.Equal("pre value another", combined.ToString());
	}
}
