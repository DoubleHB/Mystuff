// Turns a JSON sample into C# classes for System.Text.Json, one class per object shape.
// A port of the desktop app's JsonToCSharp: the two must write the same text for the same sample
// (test/fixtures/codegen holds what the desktop writes; the tests compare to the letter).

import 'dart:convert';

final _isoDateRx = RegExp(r'^\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}');
final _splitRx = RegExp(r'[^\p{L}\p{N}]+', unicode: true);
final _digitFirstRx = RegExp(r'^\p{Nd}', unicode: true);

const _keywords = {
  'abstract', 'as', 'base', 'bool', 'break', 'byte', 'case', 'catch', 'char', 'checked', 'class', 'const', 'continue', 'decimal', 'default', //
  'delegate', 'do', 'double', 'else', 'enum', 'event', 'explicit', 'extern', 'false', 'finally', 'fixed', 'float', 'for', 'foreach', 'goto', //
  'if', 'implicit', 'in', 'int', 'interface', 'internal', 'is', 'lock', 'long', 'namespace', 'new', 'null', 'object', 'operator', 'out', //
  'override', 'params', 'private', 'protected', 'public', 'readonly', 'ref', 'return', 'sbyte', 'sealed', 'short', 'sizeof', 'stackalloc', //
  'static', 'string', 'struct', 'switch', 'this', 'throw', 'true', 'try', 'typeof', 'uint', 'ulong', 'unchecked', 'unsafe', 'ushort', //
  'using', 'virtual', 'void', 'volatile', 'while',
};

class _ClassDef {
  final String name;

  /// Only there to keep the name taken (the client class itself).
  final bool hidden;
  final List<(String json, String prop, String type)> props = [];
  _ClassDef(this.name, {this.hidden = false});
}

/// Null when [json] is not valid JSON or holds no object to model.
String? generateCSharp(String json, {String rootName = 'Root'}) {
  final Object? root;
  try {
    root = jsonDecode(json);
  } on FormatException {
    return null;
  }
  final classes = <_ClassDef>[];
  final rootType = _typeOf([root], csPascal(rootName), classes);
  if (classes.isEmpty) return null;
  final sb = StringBuffer()
    ..writeln('using System.Text.Json;')
    ..writeln('using System.Text.Json.Serialization;')
    ..writeln()
    ..writeln('// var data = JsonSerializer.Deserialize<${_trimEnd(rootType, '?')}>(json);')
    ..writeln('// Types are inferred from this one response: widen int to double where a value can have decimals,')
    ..writeln('// and add ? to anything the API may leave out.');
  _render(sb, classes);
  return sb.toString().trimRight();
}

/// The classes several JSON samples need, with names kept unique across all of them (used by the client generator).
class CsClassSet {
  final List<_ClassDef> _classes = [];
  bool get isEmpty => _classes.isEmpty;

  /// The C# type for this sample (`SearchResponse`, `List<Item>`, `int`...); null when it is not valid JSON.
  String? add(String json, String rootName) {
    final Object? root;
    try {
      root = jsonDecode(json);
    } on FormatException {
      return null;
    }
    return _typeOf([root], csPascal(rootName), _classes);
  }

  void reserve(String name) => _classes.add(_ClassDef(name, hidden: true));

  String render([String indent = '']) {
    final sb = StringBuffer();
    _render(sb, _classes, indent);
    return sb.toString();
  }
}

void _render(StringBuffer sb, List<_ClassDef> classes, [String indent = '']) {
  for (final c in classes) {
    if (c.hidden) continue;
    sb
      ..writeln()
      ..writeln('${indent}public sealed class ${c.name}')
      ..writeln('$indent{');
    for (final (jsonName, prop, type) in c.props) {
      sb.writeln('$indent    [JsonPropertyName("${jsonName.replaceAll('\\', '\\\\').replaceAll('"', '\\"')}")]');
      final init = type.endsWith('?')
          ? ''
          : type.startsWith('List<')
              ? ' = [];'
              : type == 'string'
                  ? ' = "";'
                  : classes.any((k) => k.name == type)
                      ? ' = new();'
                      : '';
      sb.writeln('$indent    public $type $prop { get; set; }$init');
    }
    sb.writeln('$indent}');
  }
}

/// 'S' string, 'N' number, 'B' true or false, 'A' array, 'O' object.
String _kind(Object v) => v is String
    ? 'S'
    : v is num
        ? 'N'
        : v is bool
            ? 'B'
            : v is List
                ? 'A'
                : 'O';

const _int32Min = -2147483648, _int32Max = 2147483647;

/// The C# type that fits every sample (all values seen for one property, or all items of one array).
String _typeOf(List<Object?> samples, String nameHint, List<_ClassDef> classes) {
  final nullable = samples.isEmpty || samples.any((s) => s == null);
  final values = <Object>[for (final s in samples) ?s];
  if (values.isEmpty) return 'object?';

  final kinds = {for (final v in values) _kind(v)};
  if (kinds.length > 1) return 'JsonElement${nullable ? '?' : ''}';

  String type;
  switch (kinds.first) {
    case 'S':
      type = values.every((v) => _isoDateRx.hasMatch(v as String) && DateTime.tryParse(v) != null) ? 'DateTimeOffset' : 'string';
    case 'N':
      // jsonDecode gives an int only for a number written without a fraction or an exponent - what TryGetInt32/64 accept
      type = values.every((v) => v is int && v >= _int32Min && v <= _int32Max)
          ? 'int'
          : values.every((v) => v is int)
              ? 'long'
              : 'double';
    case 'B':
      type = 'bool';
    case 'A':
      final items = [for (final v in values) ...(v as List)];
      return 'List<${_typeOf(items, csSingular(nameHint), classes)}>${nullable ? '?' : ''}';
    default:
      type = _addClass([for (final v in values) v as Map<String, dynamic>], nameHint, classes);
  }
  return '$type${nullable ? '?' : ''}';
}

String _addClass(List<Map<String, dynamic>> objects, String nameHint, List<_ClassDef> classes) {
  var name = nameHint;
  for (var n = 2; classes.any((c) => c.name == name); n++) {
    name = '$nameHint$n';
  }
  final def = _ClassDef(name);
  classes.add(def); // reserve the slot so the root class comes first

  final order = <String>[];
  final byName = <String, List<Object?>>{};
  for (final o in objects) {
    o.forEach((k, v) {
      (byName[k] ??= (() {
        order.add(k);
        return <Object?>[];
      })())
          .add(v);
    });
  }

  final used = {name};
  for (final jsonName in order) {
    final samples = byName[jsonName]!;
    var prop = csPascal(jsonName);
    for (var n = 2; !used.add(prop); n++) {
      prop = '${csPascal(jsonName)}$n';
    }
    var type = _typeOf(samples, csPascal(jsonName), classes);
    // missing from some objects of the same shape = optional
    if (samples.length < objects.length && !type.endsWith('?')) type += '?';
    def.props.add((jsonName, prop, type));
  }
  return name;
}

bool csIsKeyword(String s) => _keywords.contains(s);

String csPascal(String s) {
  final parts = [for (final p in s.split(_splitRx)) if (p.isNotEmpty) p];
  if (parts.isEmpty) return 'Value';
  var name = parts.map((p) => _upperFirst(p)).join();
  if (_digitFirstRx.hasMatch(name)) name = '_$name';
  return _keywords.contains(name) ? '@$name' : name;
}

/// The first character upper-cased the way .NET's char.ToUpperInvariant does it: one UTF-16 unit, and only when
/// its upper case is one unit too ("ß" stays as it is).
String _upperFirst(String p) {
  final first = p.substring(0, 1), up = first.toUpperCase();
  return (up.length == 1 ? up : first) + p.substring(1);
}

String csSingular(String s) => s.endsWith('ies') && s.length > 4
    ? '${s.substring(0, s.length - 3)}y'
    : s.endsWith('ses') || s.endsWith('xes')
        ? s.substring(0, s.length - 2)
        : s.endsWith('s') && !s.endsWith('ss') && s.length > 3
            ? s.substring(0, s.length - 1)
            : '${s}Item';

String _trimEnd(String s, String ch) {
  var end = s.length;
  while (end > 0 && s[end - 1] == ch) {
    end--;
  }
  return s.substring(0, end);
}
