using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using DataBoss.Data;
using DataBoss.Linq;
using Newtonsoft.Json;

namespace DataBoss.DataPackage
{
	public partial class DataPackage : IDataPackageBuilder
	{
		public const string DefaultDelimiter = ";";

		public readonly List<TabularDataResource> Resources = new List<TabularDataResource>();

		class DataPackageResourceBuilder : IDataPackageResourceBuilder
		{
			readonly DataPackage package;
			readonly TabularDataResource resource;

			public DataPackageResourceBuilder(DataPackage package, TabularDataResource resource) {
				this.package = package;
				this.resource = resource;
			}

			public IDataPackageResourceBuilder AddResource(string name, Func<IDataReader> getData) =>
				package.AddResource(name, getData);

			public void Save(Func<string, Stream> createOutput, CultureInfo culture = null) =>
				package.Save(createOutput, culture);

			public DataPackage Serialize(CultureInfo culture) =>
				package.Serialize(culture);

			public IDataPackageResourceBuilder WithPrimaryKey(params string[] parts) {
				if(parts != null && parts.Length > 0)
					resource.Schema.PrimaryKey.AddRange(parts);
				return this;
			}

			public IDataPackageResourceBuilder WithForeignKey(DataPackageForeignKey fk) {
				if(!package.Resources.Any(x => x.Name == fk.Reference.Resource))
					throw new InvalidOperationException($"Missing resource '{fk.Reference.Resource}'");
				resource.Schema.ForeignKeys.Add(fk);
				return this;
			}

			public IDataPackageResourceBuilder WithDelimiter(string delimiter) {
				resource.Delimiter = delimiter;
				return this;
			}

			public DataPackage Done() => package;
		}

		public static DataPackage Load(string path) {
			if(path.EndsWith(".zip")) 
				return LoadZip(path);

			var datapackageRoot = path.EndsWith("datapackage.json") ? Path.GetDirectoryName(path) : path;
			return Load(x => File.OpenRead(Path.Combine(datapackageRoot, x)));
		}

		public static DataPackage Load(Func<string, Stream> openRead)
		{
			DataPackageDescription description;
			using(var reader = new JsonTextReader(new StreamReader(openRead("datapackage.json")))) {
				var json = new JsonSerializer();
				description = json.Deserialize<DataPackageDescription>(reader);
			}

			var r = new DataPackage();
			r.Resources.AddRange(description.Resources.Select(x =>
				TabularDataResource.From(x, () =>
					NewCsvDataReader(
						new StreamReader(openRead(x.Path)),
						x.Dialect?.Delimiter,
						x.Schema))));

			return r;
		}

		public static DataPackage LoadZip(string path) =>
			LoadZip(BoundMethod.Bind(File.OpenRead, path));
 	
		public static DataPackage LoadZip(Func<Stream> openZip) {
			var r = new DataPackage();
			var description = LoadZipPackageDescription(openZip);
			r.Resources.AddRange(description.Resources.Select(x => 
				TabularDataResource.From(x, new ZipResource(openZip, x).GetData)));

			return r;
		}

		class ZipResource
		{
			readonly Func<Stream> openZip;
			readonly DataPackageResourceDescription resource;

			public ZipResource(Func<Stream> openZip, DataPackageResourceDescription resource) {
				this.openZip = openZip;
				this.resource = resource;
			}

			public IDataReader GetData() {
				var source = new ZipArchive(openZip(), ZipArchiveMode.Read);
				var csv = NewCsvDataReader(
					new StreamReader(new BufferedStream(source.GetEntry(resource.Path).Open(), 81920)),
					resource.Dialect?.Delimiter,
					resource.Schema);
				csv.Disposed += delegate { source.Dispose(); };
				return csv;
			}
		}

		static DataPackageDescription LoadZipPackageDescription(Func<Stream> openZip) {
			var json = new JsonSerializer();
			using (var zip = new ZipArchive(openZip(), ZipArchiveMode.Read))
			using(var reader = new JsonTextReader(new StreamReader(zip.GetEntry("datapackage.json").Open())))
				return json.Deserialize<DataPackageDescription>(reader);
		}

		static CsvDataReader NewCsvDataReader(TextReader reader, string delimiter, TabularDataSchema schema) =>
			new CsvDataReader(
				new CsvHelper.CsvReader(
					reader,
					new CsvHelper.Configuration.CsvConfiguration(CultureInfo.InvariantCulture) { Delimiter = delimiter }),
				schema);

		public IDataPackageResourceBuilder AddResource(string name, Func<IDataReader> getData)
		{
			var resource = TabularDataResource.From(
				new DataPackageResourceDescription {
					Name = name,
					Schema = new TabularDataSchema {
						PrimaryKey = new List<string>(),
						ForeignKeys = new List<DataPackageForeignKey>(),
					},
				}, getData);
			Resources.Add(resource);
			return new DataPackageResourceBuilder(this, resource);
		}

		DataPackage IDataPackageBuilder.Done() => this;

		public void UpdateResource(string name, Func<TabularDataResource, TabularDataResource> doUpdate) {
			var found = Resources.FindIndex(x => x.Name == name);
			if(found == -1)
				throw new InvalidOperationException($"Resource '{name}' not found.");
			Resources[found] = doUpdate(Resources[found]);
		}

		public void TransformResource(string name, Action<DataReaderTransform> defineTransform) =>
			UpdateResource(name, xs => xs.Transform(defineTransform));

		public TabularDataResource GetResource(string name) => Resources.Single(x => x.Name == name);

		public void Save(Func<string, Stream> createOutput, CultureInfo culture = null) {
			var description = new DataPackageDescription();
			var decimalCharOverride = culture != null ? culture.NumberFormat.NumberDecimalSeparator : null;
			var defaultFormatter = new RecordFormatter(culture ?? CultureInfo.InvariantCulture);

			foreach (var item in Resources) {
				var resourcePath = $"{item.Name}.csv";
				using (var output = createOutput(resourcePath))
				using (var data = item.Read()) {
					var fieldCount = item.Schema.Fields.Count;
					var fields = new List<TabularDataSchemaFieldDescription>(fieldCount);
					var toString = new Func<IDataRecord, int, string>[fieldCount];

					for (var i = 0; i != fieldCount; ++i) {
						var field = item.Schema.Fields[i];
						var fieldFormatter = defaultFormatter;
						if (field.IsNumber()) {
							field = new TabularDataSchemaFieldDescription(
								field.Name,
								field.Type,
								constraints: field.Constraints,
								decimalChar: decimalCharOverride ?? field.DecimalChar);
							fieldFormatter = new RecordFormatter(field.GetNumberFormat());
						}

						fields.Add(field);
						toString[i] = fieldFormatter.GetFormatter(data.GetFieldType(i), field);
					}

					var delimiter = item.Delimiter ?? DefaultDelimiter;
					description.Resources.Add(new DataPackageResourceDescription {
						Name = item.Name, 
						Path = Path.GetFileName(resourcePath),
						Dialect = new CsvDialectDescription { Delimiter = delimiter },
						Schema = new TabularDataSchema { 
							Fields = fields,
							PrimaryKey = NullIfEmpty(item.Schema.PrimaryKey),
							ForeignKeys = NullIfEmpty(item.Schema.ForeignKeys),
						},
					});
					try {
						WriteRecords(output, delimiter, data, toString);
					} catch(Exception ex) {
						throw new Exception($"Failed writing {item.Name}.", ex);
					}
				}
			};

			using (var meta = new StreamWriter(createOutput("datapackage.json")))
				meta.Write(JsonConvert.SerializeObject(description, Formatting.Indented));
		}

		public DataPackage Serialize(CultureInfo culture = null) {
			var bytes = new MemoryStream();
			this.SaveZip(bytes, culture);
			bytes.TryGetBuffer(out var buffer);
			return LoadZip(() => new MemoryStream(buffer.Array, buffer.Offset, buffer.Count, false));
		}

		static List<T> NullIfEmpty<T>(List<T> values) =>
			values == null ? null : values.Count == 0 ? null : values;

		static CsvWriter NewCsvWriter(Stream stream, Encoding encoding, string delimiter) => 
			new CsvWriter(new StreamWriter(stream, encoding, 4096, leaveOpen: true), delimiter);

		static void WriteRecords(Stream output, string delimiter, IDataReader data, Func<IDataRecord, int, string>[] toString) {

			WriteHeaderRecord(output, Encoding.UTF8, delimiter, data);

			var records = Channel.CreateBounded<IReadOnlyCollection<IDataRecord>>(new BoundedChannelOptions(1024) {
				SingleWriter = true,
			});

			var chunks = Channel.CreateBounded<MemoryStream>(new BoundedChannelOptions(1024) {
				SingleWriter = false,
				SingleReader = true,
			});

			var reader = new RecordReader(data.AsDataRecordReader(), records);
			var writer = new ChunkWriter(records, chunks, new UTF8Encoding(false)) {
				Delimiter = delimiter,
				FormatValue = toString,					
			};

			reader.Start();
			writer.Start();

			chunks.Reader.ForEach(x => x.CopyTo(output));

			if (reader.Error != null)
				throw new Exception("Failed to write csv", reader.Error);
			if (writer.Error != null)
				throw new Exception("Failed to write csv", writer.Error);
		}

		static void WriteHeaderRecord(Stream output, Encoding encoding, string delimiter, IDataReader data) {
			using (var csv = NewCsvWriter(output, encoding, delimiter)) {
				for (var i = 0; i != data.FieldCount; ++i)
					csv.WriteField(data.GetName(i));
				csv.NextRecord();
				csv.Writer.Flush();
			}
		}

		abstract class WorkItem
		{
			Thread thread;
			public Exception Error { get; private set; }

			protected abstract void DoWork();
			protected virtual void Cleanup() { }

			public void Start() {
				if(thread != null)
					throw new InvalidOperationException("WorkItem already started.");
				thread = new Thread(Run) {
					IsBackground = true,
					Name = GetType().Name,
				};
				thread.Start();
			}

			void Run() {
				try {
					DoWork();
				} catch (Exception ex) {
					Error = ex;
				} finally {
					Cleanup();
					thread = null;
				}
			}
		}

		class RecordReader : WorkItem
		{
			public const int BufferRows = 8192;

			readonly IDataRecordReader reader;
			readonly ChannelWriter<IReadOnlyCollection<IDataRecord>> writer;
 
			public RecordReader(IDataRecordReader reader, ChannelWriter<IReadOnlyCollection<IDataRecord>> writer) {
				this.reader = reader;
				this.writer = writer;
			}

			protected override void DoWork() {
				var values = CreateBuffer();
				var n = 0;
				while (reader.Read()) {
					values.Add(reader.GetRecord());

					if (++n == BufferRows) {
						writer.Write(values);
						n = 0;
						values = CreateBuffer();
					}
				}

				if (n != 0)
					writer.Write(values);
			}

			List<IDataRecord> CreateBuffer() => new List<IDataRecord>(BufferRows);

			protected override void Cleanup() =>
				writer.Complete();
		}

		class ChunkWriter : WorkItem
		{
			readonly ChannelReader<IReadOnlyCollection<IDataRecord>> records;
			readonly ChannelWriter<MemoryStream> chunks;
			readonly Encoding encoding;
			readonly int bomLength;
			int chunkCapacity = 4 * 4096; 

			public ChunkWriter(ChannelReader<IReadOnlyCollection<IDataRecord>> records, ChannelWriter<MemoryStream> chunks, Encoding encoding) {
				this.records = records;
				this.chunks = chunks;
				this.encoding = encoding;
				this.bomLength = encoding.GetPreamble().Length;
			}

			public Func<IDataRecord, int, string>[] FormatValue;
			public string Delimiter;
			public int MaxWorkers = 1;

			protected override void DoWork() {
				if (MaxWorkers == 1)
					records.ForEach(WriteRecords);
				else 
					records.GetConsumingEnumerable().AsParallel()
						.WithDegreeOfParallelism(MaxWorkers)
						.ForAll(WriteRecords);
			}

			void WriteRecords(IReadOnlyCollection<IDataRecord> item) {
				if (item.Count == 0)
					return;

				var chunk = new MemoryStream(chunkCapacity);
				using (var fragment = NewCsvWriter(chunk, encoding, Delimiter)) {
					foreach (var r in item) {
						for (var i = 0; i != r.FieldCount; ++i) {
							if (r.IsDBNull(i))
								fragment.NextField();
							else
								fragment.WriteField(FormatValue[i](r, i));
						}
						fragment.NextRecord();
					}
				}
				if (chunk.Position != 0) {
					chunkCapacity = Math.Max(chunkCapacity, chunk.Capacity);
					WriteChunk(chunk);
				}
			}

			void WriteChunk(MemoryStream chunk) {
				chunk.Position = bomLength;
				chunks.Write(chunk);
			}

			protected override void Cleanup() =>
				chunks.Complete();
		}
	}
}
