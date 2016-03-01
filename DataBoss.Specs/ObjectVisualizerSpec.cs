﻿using System.IO;
using Cone;
using DataBoss.Data;

namespace DataBoss.Specs
{
	[Describe(typeof(ObjectVisualizer))]
	public class ObjectVisualizerSpec
	{
		[Context("Dump")]
		public class ObjectVisualizer_Dump
		{
			StringWriter Output;
			ObjectVisualizer Visualizer;

			[BeforeEach]
			public void ResetOutput() {
				Output = new StringWriter();
				Visualizer = new ObjectVisualizer(Output);
			}

			string Dump<T>(T value) {
				Visualizer.Append(value);
				return Output.ToString();
			}

			public void puts_each_object_on_single_line() {
				Dump("Hello World!");
				Dump(42);
				Check.That(() => Output.ToString() == "Hello World!\r\n42\r\n");
			}

			public void outputs_row_per_item_in_sequence() {
				Check.That(() => Dump(new[] { 1, 2, 3 }) == "1\r\n2\r\n3\r\n");
			}

			public void displays_prop_and_value_for_single_object() {
				Check.That(() => Dump(new { Hello = "World" }) == "Hello: World\r\n");
			}

			public void aligns_members_for_single_object() {
				Check.That(() => Dump(
					new {
						First = "One",
						Two = "Second",
					}) == Lines(
						"First: One",
						"Two  : Second"
					));
			}

			public void nested_object() {
				Check.That(() => Dump(
					new {
						Values = new[] { 1, 2, 3 },
					}) == Lines(
						"Values: 1",
						"      : 2",
						"      : 3"
					));
			}

			public void multiline_string() {
				Check.That(() => Dump(
					new {
						Greeting = "Hello\r\nWorld",
					}) == Lines(
						"Greeting: Hello",
						"        : World"
					));
			}

			string Lines(params string[] lines){
				return string.Join("\r\n", lines) + "\r\n";
			}

			public void uses_prop_name_as_heading_for_sequence() {
				Check.That(() => Dump(
					new[] {
						new { Hello = "Row 1" },
						new { Hello = "Row 2" }
					}) == "Hello\r\nRow 1\r\nRow 2\r\n");
			}

			public void delimits_columns() {
				Check.That(() => Dump(
					new[] {
						new { Hello = "Bar", World = 1 },
						new { Hello = "Row 2", World = 42 }
					}) == "Hello │ World\r\nBar   │     1\r\nRow 2 │    42\r\n");
			}

			public void adjusts_column_widhts_to_biggest_row() {
				Check.That(() => Dump(
					new[] {
						new { A = "1", B = 2 },
						new { A= "234", B = 43 }
					}) == "A   │ B\r\n1   │  2\r\n234 │ 43\r\n");
			}

			public void treats_IDataReader_like_sequence() {
				Dump(SequenceDataReader.Create(new[] {
					new { ABC = "1", B = 2 },
					new { ABC = "23", B = 43 }
				}, "ABC", "B"));
				Check.That(() => Output.ToString() == "ABC │ B\r\n1   │  2\r\n23  │ 43\r\n");
			}

			public void right_justifies_numbers() {
				Dump(SequenceDataReader.Create(new[] {
					new { ABC = 1 },
				}, "ABC"));
				Check.That(() => Output.ToString() == "ABC\r\n  1\r\n");
			}
		}
	}
}
