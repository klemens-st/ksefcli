using System.Reflection;
using System.Xml;
using System.Xml.Schema;

namespace KCKSeFCli;

public static class XmlValidator {
    private static XmlSchemaSet? _schema;

    /// <summary>
    /// The FA(3) schema and its full import chain, leaf first.
    ///
    /// schemat_FA(3) imports StrukturyDanych by absolute http:// URL, StrukturyDanych includes
    /// ElementarneTypyDanych the same way, and ElementarneTypyDanych includes KodyKrajow by a
    /// relative path. Registering every one of them here means no schemaLocation is ever
    /// dereferenced, so validation runs offline and cannot be steered by whoever answers for
    /// crd.gov.pl. Leaf-first ordering keeps type references resolvable as each schema is added.
    /// </summary>
    private static readonly string[] SchemaResources = {
        "KCKSeFCli.Resources.KodyKrajow_v10-0E.xsd",
        "KCKSeFCli.Resources.ElementarneTypyDanych_v10-0E.xsd",
        "KCKSeFCli.Resources.StrukturyDanych_v10-0E.xsd",
        "KCKSeFCli.Resources.schemat_FA(3)_v1-0E.xsd",
    };

    /// <summary>
    /// Reader settings for untrusted input: no DTDs, no external resolution.
    /// </summary>
    private static XmlReaderSettings SecureReaderSettings() => new() {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
    };

    private static XmlSchemaSet GetSchema() {
        if (_schema != null)
            return _schema;

        XmlSchemaSet set = new() {
            // Suppress external fetches. Every schema in the chain is supplied explicitly below.
            XmlResolver = null,
        };

        Assembly assembly = Assembly.GetExecutingAssembly();
        foreach (string resourceName in SchemaResources) {
            using Stream? stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
                throw new Exception($"Embedded resource not found: {resourceName}");
            using XmlReader reader = XmlReader.Create(stream, SecureReaderSettings());
            XmlSchema schema = XmlSchema.Read(reader, null)
                ?? throw new Exception($"Could not read schema: {resourceName}");
            set.Add(schema);
        }

        // Compile eagerly so an incomplete chain fails here, loudly, rather than degrading into
        // "type is not declared" errors against every invoice that gets validated.
        set.Compile();

        _schema = set;
        return _schema;
    }

    public static bool Validate(string xml, out List<string> errors) {
        List<string> localErrors = new List<string>();
        XmlReaderSettings settings = SecureReaderSettings();
        settings.Schemas = GetSchema();
        settings.ValidationType = ValidationType.Schema;
        settings.ValidationFlags = XmlSchemaValidationFlags.ReportValidationWarnings;
        settings.ValidationEventHandler += (sender, args) => localErrors.Add(args.Message);

        try {
            using (XmlReader reader = XmlReader.Create(new StringReader(xml), settings)) {
                while (reader.Read()) { }
            }
        } catch (XmlException ex) {
            localErrors.Add(ex.Message);
        }

        errors = localErrors;
        return errors.Count == 0;
    }

    public static bool ValidateLog(string xml, out List<string> errors) {
        bool isValid = Validate(xml, out errors);
        if (!isValid) {
            Log.Error("XML validation failed:");
            foreach (string error in errors) {
                Log.Error(error);
            }
        }
        return isValid;
    }
}
