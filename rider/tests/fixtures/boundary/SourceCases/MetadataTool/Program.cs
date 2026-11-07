using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

// This executable reads PE/PDB bytes only. It never loads or executes a fixture assembly.
if (args.Length != 3 || args[0] is not ("inspect" or "patch-broken")) throw new ArgumentException("inspect|patch-broken SourceCasesRoot case");
var root = Path.GetFullPath(args[1]);
if (!root.Replace('\\','/').EndsWith("/rider/tests/fixtures/boundary/SourceCases", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Owned assets only");
var name = args[2];
if (!new[] { "offline", "unauthorized", "mismatch", "nosymbols", "broken" }.Contains(name)) throw new ArgumentException("Unknown case");
void NoLinks(string path)
{
    for (string? current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
        if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Linked/reparse paths are not owned diagnostic assets");
}
var dll = Path.Combine(root, "artifacts", "dll", "Issue34.Source." + name + ".dll");
NoLinks(dll);
string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
var bytes = File.ReadAllBytes(dll);
object? patch = null;
if (args[0] == "patch-broken")
{
    if (name != "broken") throw new ArgumentException("Only the owned broken-IL asset may be patched");
    using var original = new PEReader(new MemoryStream(bytes, false));
    var metadata = original.GetMetadataReader();
    var method = metadata.MethodDefinitions.Select(handle => metadata.GetMethodDefinition(handle))
        .Single(definition => metadata.GetString(definition.Name) == "RenderProbe");
    var rva = method.RelativeVirtualAddress;
    var section = original.PEHeaders.SectionHeaders.Single(section => rva >= section.VirtualAddress && rva < section.VirtualAddress + section.SizeOfRawData);
    int header = section.PointerToRawData + rva - section.VirtualAddress;
    int size = (bytes[header] & 3) == 2 ? 1 : (bytes[header + 1] >> 4) * 4;
    int length = original.GetMethodBody(rva).GetILBytes()!.Length;
    if (length < 4) throw new InvalidDataException("Patch target too small");
    string before = Sha(bytes);string metadataBefore = Sha(original.GetMetadata().GetContent().ToArray());int code = header + size;
    // Reserved extended opcode, then reserved opcodes. Keep PE headers, metadata and method signatures intact.
    bytes[code] = 0xfe;bytes[code + 1] = 0xff;Array.Fill(bytes, (byte)0xff, code + 2, length - 2);
    using var patched = new PEReader(new MemoryStream(bytes, false));
    bool metadataBytesUnchanged = metadataBefore == Sha(patched.GetMetadata().GetContent().ToArray());
    if (!metadataBytesUnchanged) throw new InvalidDataException("Patch unexpectedly altered metadata");
    File.WriteAllBytes(dll, bytes);
    patch = new { method = "RenderProbe", rva, codeFileOffset = code, length, beforeSha256 = before, afterSha256 = Sha(bytes), metadataBytesUnchanged,
        scope = "invalid IL only; no claim that Rider's resilient decompiler must fail" };
}
using var pe = new PEReader(new MemoryStream(bytes, false));
var reader = pe.GetMetadataReader();
var definition = reader.GetAssemblyDefinition();
string AttributeName(CustomAttribute attribute)
{
    EntityHandle parent = attribute.Constructor.Kind == HandleKind.MemberReference
        ? reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent
        : reader.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor).GetDeclaringType();
    return parent.Kind == HandleKind.TypeReference ? reader.GetString(reader.GetTypeReference((TypeReferenceHandle)parent).Name)
        : parent.Kind == HandleKind.TypeDefinition ? reader.GetString(reader.GetTypeDefinition((TypeDefinitionHandle)parent).Name) : "";
}
var witnesses = new List<object>();
foreach (var handle in reader.CustomAttributes)
{
    var attribute = reader.GetCustomAttribute(handle);
    if (AttributeName(attribute) != "FSMNodeSourceAttribute") continue;
    var blob = reader.GetBlobReader(attribute.Value);
    if (blob.ReadUInt16() != 1) throw new InvalidDataException("Invalid attribute prolog");
    string? path = blob.ReadSerializedString();
    witnesses.Add(new { attribute = "FSMNodeSourceAttribute", sourceCSPath = path, read = "metadata constructor argument only, not an invoked getter or source identity proof" });
}
var pdb = Path.ChangeExtension(dll, ".pdb");NoLinks(pdb);
var debugEntries = pe.ReadDebugDirectory().Select(entry => entry.Type == DebugDirectoryEntryType.CodeView
    ? (object)new { kind = entry.Type.ToString(), guid = pe.ReadCodeViewDebugDirectoryData(entry).Guid, age = pe.ReadCodeViewDebugDirectoryData(entry).Age }
    : new { kind = entry.Type.ToString() }).ToArray();
var documents = new List<object>();var sourceLinks = new List<string>();bool embeddedSource = false;bool? pdbIdentityMatches = null;
if (File.Exists(pdb))
{
    using var provider = MetadataReaderProvider.FromPortablePdbStream(File.OpenRead(pdb));
    var symbols = provider.GetMetadataReader();
    var id = symbols.DebugMetadataHeader!.Id.ToArray();
    var guid = new Guid(id.Take(16).ToArray());
    var codeviews = pe.ReadDebugDirectory().Where(entry => entry.Type == DebugDirectoryEntryType.CodeView).ToArray();
    pdbIdentityMatches = codeviews.Length == 1 && pe.ReadCodeViewDebugDirectoryData(codeviews[0]).Guid == guid;
    foreach (var handle in symbols.Documents)
    {
        var document = symbols.GetDocument(handle);string path = symbols.GetString(document.Name);
        var hash = symbols.GetBlobBytes(document.Hash);var algorithm = symbols.GetGuid(document.HashAlgorithm);
        // Only read the one explicitly owned local candidate, never follow arbitrary PDB document paths.
        var candidate = Path.Combine(root, "candidates", "mismatch.cs");NoLinks(candidate);
        bool ownedCandidate = string.Equals(path.Replace('\\','/'), candidate.Replace('\\','/'), StringComparison.OrdinalIgnoreCase);
        bool? matches = ownedCandidate && algorithm == new Guid("8829d00f-11b8-4213-878b-770e8597ac16") ? SHA256.HashData(File.ReadAllBytes(candidate)).SequenceEqual(hash) : null;
        documents.Add(new { name = path, algorithm, checksum = Convert.ToHexString(hash).ToLowerInvariant(), ownedCandidate, candidateChecksumMatches = matches });
    }
    foreach (var handle in symbols.CustomDebugInformation)
    {
        var info = symbols.GetCustomDebugInformation(handle);var kind = symbols.GetGuid(info.Kind);
        if (kind == new Guid("cc110556-a091-4d38-9fec-25ab9a351a6a")) sourceLinks.Add(Encoding.UTF8.GetString(symbols.GetBlobBytes(info.Value)));
        if (kind == new Guid("0e8a571b-6926-466e-b4ad-8ab04611f5fe")) embeddedSource = true;
    }
}
Console.WriteLine(JsonSerializer.Serialize(new {
    name, dllSha256 = Sha(bytes), assembly = reader.GetString(definition.Name), version = definition.Version.ToString(),
    mvid = reader.GetGuid(reader.GetModuleDefinition().Mvid), metadataReadable = true,
    types = reader.TypeDefinitions.Select(handle => reader.GetString(reader.GetTypeDefinition(handle).Name)).ToArray(),
    debugEntries, pdbPresent = File.Exists(pdb), pdbIdentityMatches, documents, sourceLinks, embeddedSource, witnesses, patch,
    scope = "PE/PDB bytes only; absence/mismatch of these assets is not offline/permission diagnosis or host navigation acceptance"
}, new JsonSerializerOptions { WriteIndented = true }));
