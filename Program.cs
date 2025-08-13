using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CSharpToDartModelGenerator
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: CSharpToDartModelGenerator <input-cs-file> <output-dart-file>");
                return;
            }

            string inputFile = args[0];
            string outputFile = args[1];

            // Read the C# code from file
            string csCode = File.ReadAllText(inputFile);

            // Parse the C# code into a syntax tree
            SyntaxTree tree = CSharpSyntaxTree.ParseText(csCode);
            CompilationUnitSyntax root = tree.GetCompilationUnitRoot();

            // Find the class declaration (assuming one class per file)
            ClassDeclarationSyntax? classDecl = root.DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .FirstOrDefault();

            if (classDecl == null)
            {
                Console.WriteLine("No class found in the input file.");
                return;
            }

            string className = classDecl.Identifier.Text;
            string dartClassName = className + "Model"; // Generalize: append "Model"

            // Extract properties
            var properties = classDecl.Members.OfType<PropertyDeclarationSyntax>().ToList();

            // Generate Dart code
            string dartCode = GenerateDartClass(dartClassName, properties);

            // Write to output file
            File.WriteAllText(outputFile, dartCode);

            Console.WriteLine($"Dart model generated at: {outputFile}");
        }

        private static string GenerateDartClass(string dartClassName, List<PropertyDeclarationSyntax> properties)
        {
            var sb = new StringBuilder();

            // Imports and annotations
            sb.AppendLine("@JsonSerializable()");
            sb.AppendLine($"class {dartClassName} extends Equatable implements Jsonable {{");

            // Fields
            foreach (var prop in properties)
            {
                string propName = prop.Identifier.Text;
                string csType = prop.Type.ToString();
                string dartType = MapCSharpTypeToDart(csType);
                string fieldName = ToCamelCase(propName);
                bool isNullable = csType.EndsWith("?") || IsNullableValueType(csType);
                bool isRequired = prop.Modifiers.Any(m => m.Text == "required") || (!isNullable && !csType.EndsWith("?"));

                sb.AppendLine($"  @JsonKey(name: \"{propName}\")");
                sb.AppendLine($"  final {dartType} {fieldName};");
            }

            // Constructor
            sb.AppendLine();
            sb.Append($"  {dartClassName}({{");
            foreach (var prop in properties)
            {
                string propName = prop.Identifier.Text;
                string csType = prop.Type.ToString();
                string fieldName = ToCamelCase(propName);
                bool isNullable = csType.EndsWith("?") || IsNullableValueType(csType);
                bool isRequired = prop.Modifiers.Any(m => m.Text == "required") || (!isNullable && !csType.EndsWith("?"));

                sb.AppendLine();
                sb.Append($"    {(isRequired ? "required this." : "this.")}{fieldName},");
            }
            sb.AppendLine();
            sb.AppendLine("  });");

            // fromJson and toJson
            sb.AppendLine();
            sb.AppendLine($"  factory {dartClassName}.fromJson(Map<String, dynamic> json) =>");
            sb.AppendLine($"      _${dartClassName}FromJson(json);");
            sb.AppendLine();
            sb.AppendLine($"  Map<String, dynamic> toJson() => _${dartClassName}ToJson(this);");

            // updateJsonable
            sb.AppendLine();
            sb.AppendLine("  @override");
            sb.AppendLine("  T updateJsonable<T extends Jsonable>(");
            sb.AppendLine("    String columnName,");
            sb.AppendLine("    dynamic newCellValue,");
            sb.AppendLine("  ) {");
            sb.AppendLine("    Map<String, dynamic> updatedData = Map<String, dynamic>.from(toJson());");
            sb.AppendLine("    updatedData[columnName] = newCellValue;");
            sb.AppendLine($"    return {dartClassName}.fromJson(updatedData) as T;");
            sb.AppendLine("  }");

            // props
            sb.AppendLine();
            sb.AppendLine("  @override");
            sb.AppendLine("  List<Object?> get props => [");
            foreach (var prop in properties)
            {
                string fieldName = ToCamelCase(prop.Identifier.Text);
                sb.AppendLine($"    {fieldName},");
            }
            sb.AppendLine("  ];");

            sb.AppendLine("}");

            return sb.ToString();
        }

        private static string MapCSharpTypeToDart(string csType)
        {
            bool isNullable = csType.EndsWith("?");
            if (isNullable) csType = csType.Substring(0, csType.Length - 1);

            // Handle arrays
            if (csType.EndsWith("[]"))
            {
                string innerCsType = csType.Substring(0, csType.Length - 2);
                string innerDartType = MapCSharpTypeToDart(innerCsType);
                string listType = $"List<{innerDartType}>";
                return isNullable ? listType + "?" : listType;
            }

            // Handle generics like List<T>
            if (csType.StartsWith("List<") && csType.EndsWith(">"))
            {
                string innerCsType = csType.Substring(5, csType.Length - 6);
                string innerDartType = MapCSharpTypeToDart(innerCsType);
                string listType = $"List<{innerDartType}>";
                return isNullable ? listType + "?" : listType;
            }

            // Handle Dictionary<K,V>
            if (csType.StartsWith("Dictionary<") && csType.EndsWith(">"))
            {
                string generics = csType.Substring(11, csType.Length - 12);
                var parts = generics.Split(new char[] { ',' }, 2);
                if (parts.Length == 2)
                {
                    string keyDart = MapCSharpTypeToDart(parts[0].Trim());
                    string valDart = MapCSharpTypeToDart(parts[1].Trim());
                    string mapType = $"Map<{keyDart}, {valDart}>";
                    return isNullable ? mapType + "?" : mapType;
                }
            }

            // Basic type mapping
            string dartType = csType switch
            {
                "int" => "int",
                "long" => "int",
                "short" => "int",
                "byte" => "int",
                "bool" => "bool",
                "double" => "double",
                "float" => "double",
                "decimal" => "double",
                "string" => "String",
                "DateTime" => "DateTime",
                "DateOnly" => "DateTime",
                "TimeOnly" => "Duration",
                "byte[]" => "Uint8List",
                _ => csType // Assume custom type or enum
            };

            return isNullable ? dartType + "?" : dartType;
        }

        private static bool IsNullableValueType(string csType)
        {
            return csType.EndsWith("?") && !csType.StartsWith("string");
        }

        private static string ToCamelCase(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return char.ToLowerInvariant(s[0]) + s.Substring(1);
        }
    }
}