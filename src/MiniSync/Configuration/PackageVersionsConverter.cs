// Copyright (c) 2026 Linus Berg. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace MiniSync.Configuration;

/// <summary>
///   Reads a package map whose values are either a version string or an array
///   of version strings, and writes single versions back as plain strings.
/// </summary>
public class PackageVersionsConverter : JsonConverter<Dictionary<string, List<string>>> {
  /// <inheritdoc />
  public override Dictionary<string, List<string>> Read(ref Utf8JsonReader reader,
    Type type_to_convert, JsonSerializerOptions options) {
    if (reader.TokenType != JsonTokenType.StartObject) {
      throw new JsonException("packages must be an object");
    }

    Dictionary<string, List<string>> packages = new();
    while (reader.Read() && reader.TokenType != JsonTokenType.EndObject) {
      string name = reader.GetString()!;
      reader.Read();
      List<string> versions = new();
      if (reader.TokenType == JsonTokenType.String) {
        versions.Add(reader.GetString()!);
      } else if (reader.TokenType == JsonTokenType.StartArray) {
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray) {
          if (reader.TokenType != JsonTokenType.String) {
            throw new JsonException($"versions of {name} must be strings");
          }

          versions.Add(reader.GetString()!);
        }
      } else {
        throw new JsonException(
          $"{name} must be a version string or an array of version strings"
        );
      }

      packages[name] = versions;
    }

    return packages;
  }

  /// <inheritdoc />
  public override void Write(Utf8JsonWriter writer,
                             Dictionary<string, List<string>> packages,
                             JsonSerializerOptions options) {
    writer.WriteStartObject();
    foreach (KeyValuePair<string, List<string>> package in packages) {
      writer.WritePropertyName(package.Key);
      if (package.Value.Count == 1) {
        writer.WriteStringValue(package.Value[0]);
        continue;
      }

      writer.WriteStartArray();
      foreach (string version in package.Value) {
        writer.WriteStringValue(version);
      }

      writer.WriteEndArray();
    }

    writer.WriteEndObject();
  }
}
