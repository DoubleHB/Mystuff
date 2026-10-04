// The screens added in 1.6, driven as widgets: picking APIs on the list, the comparison, the C# pages.
//   flutter test test/pages_test.dart
// With APIFREE_SHOTS set to a folder, each step is also saved there as a PNG - a look at the screens without a build.
// ignore_for_file: depend_on_referenced_packages
import 'dart:io';
import 'dart:ui' as ui;

import 'package:apiscout_mobile/code_page.dart';
import 'package:apiscout_mobile/compare_page.dart';
import 'package:apiscout_mobile/json_to_csharp.dart';
import 'package:apiscout_mobile/knowledge.dart';
import 'package:apiscout_mobile/main.dart';
import 'package:apiscout_mobile/models.dart';
import 'package:apiscout_mobile/user_data.dart';
import 'package:apiscout_mobile/widgets.dart';
import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:path_provider_platform_interface/path_provider_platform_interface.dart';
import 'package:plugin_platform_interface/plugin_platform_interface.dart';
import 'package:shared_preferences/shared_preferences.dart';

/// The app's files go to a temporary folder, never to this PC's real application data.
class _TempPaths extends Fake with MockPlatformInterfaceMixin implements PathProviderPlatform {
  final String dir;
  _TempPaths(this.dir);
  @override
  Future<String?> getApplicationSupportPath() async => dir;
  @override
  Future<String?> getApplicationDocumentsPath() async => dir;
  @override
  Future<String?> getTemporaryPath() async => dir;
}

Future<void> _loadFonts() async {
  Future<ByteData> bytes(String path) async => ByteData.sublistView(await File(path).readAsBytes());
  for (final (family, weights) in [('Manrope', ['400', '500', '600', '700', '800']), ('JetBrainsMono', ['400', '500', '700'])]) {
    final loader = FontLoader(family);
    for (final w in weights) {
      loader.addFont(bytes('assets/fonts/$family-$w.ttf'));
    }
    await loader.load();
  }
  final icons = File('${Platform.environment['FLUTTER_ROOT']}/bin/cache/artifacts/material_fonts/MaterialIcons-Regular.otf');
  if (icons.existsSync()) await (FontLoader('MaterialIcons')..addFont(bytes(icons.path))).load();
}

const _shotKey = ValueKey('shot');

Future<void> _shot(WidgetTester tester, String name) async {
  final dir = Platform.environment['APIFREE_SHOTS'];
  if (dir == null) return;
  await tester.runAsync(() async {
    final boundary = tester.renderObject<RenderRepaintBoundary>(find.byKey(_shotKey));
    final image = await boundary.toImage(pixelRatio: 1.5);
    final png = await image.toByteData(format: ui.ImageByteFormat.png);
    File('$dir/$name.png').writeAsBytesSync(png!.buffer.asUint8List());
  });
}

Widget _app(Widget home, {Brightness brightness = Brightness.light}) =>
    RepaintBoundary(key: _shotKey, child: MaterialApp(debugShowCheckedModeBanner: false, theme: ledgerTheme(brightness), home: home));

void _phone(WidgetTester tester) {
  tester.view.physicalSize = const Size(1080, 2400);
  tester.view.devicePixelRatio = 2.625;
  addTearDown(tester.view.reset);
}

ApiEntry _entry(String name, String url, {AuthKind auth = AuthKind.unknown, String desc = '', bool? https = true, String cors = '', int? health, List<String> sources = const ['public-apis']}) =>
    ApiEntry(key: Uri.parse(url).host.replaceFirst('www.', '') + Uri.parse(url).path.replaceFirst(RegExp(r'/$'), ''), name: name, url: url, auth: auth, description: desc, https: https, cors: cors, health: health, sources: sources, category: 'Science');

/// A name in a list row (the API of the day card above the list can show the same name).
Finder row(String name) => find.descendant(of: find.byType(ApiTile), matching: find.text(name));

void main() {
  late Knowledge k;

  setUpAll(() async {
    TestWidgetsFlutterBinding.ensureInitialized();
    await _loadFonts();
    PathProviderPlatform.instance = _TempPaths(Directory.systemTemp.createTempSync('apifree-pages-').path);
    SharedPreferences.setMockInitialValues({});
    k = Knowledge.parse(File('assets/knowledge.json').readAsStringSync());
  });

  testWidgets('a long press picks APIs on the list; Compare lays them side by side', (tester) async {
    _phone(tester);
    await tester.runAsync(state.load);
    state.useCatalogue(Catalogue(DateTime(2026, 10, 4, 9), [
      _entry('Cat Facts', 'https://catfact.ninja', auth: AuthKind.none, desc: 'Daily cat facts', cors: 'No', health: 99),
      _entry('GitHub', 'https://docs.github.com/en/rest', auth: AuthKind.oauth, desc: 'Repositories, code and user info', cors: 'Yes'),
      _entry('NASA', 'https://api.nasa.gov', desc: 'NASA data, including imagery', sources: ['n0shake']),
      _entry('Open-Meteo', 'https://open-meteo.com', auth: AuthKind.none, desc: 'Global weather forecast API for non-commercial use', cors: 'Yes', health: 100),
      _entry('TheCocktailDB', 'https://www.thecocktaildb.com/api.php', auth: AuthKind.apiKey, desc: 'Cocktail recipes', cors: 'Yes', health: 95, sources: ['public-apis', 'public-api-lists', 'publicapis.dev', 'freepublicapis']),
    ]));
    await tester.pumpWidget(_app(const HomePage()));
    await tester.pump(const Duration(milliseconds: 300));
    expect(row('NASA'), findsOneWidget);
    expect(find.byType(PickBar), findsNothing);

    await tester.longPress(row('NASA'));
    await tester.pump(const Duration(milliseconds: 300));
    expect(find.textContaining('1 picked'), findsOneWidget);
    expect(find.textContaining('one more'), findsOneWidget);
    await _shot(tester, '1-picking-one');

    // while picking, a tap ticks instead of opening the API
    await tester.tap(row('TheCocktailDB'));
    await tester.pump(const Duration(milliseconds: 300));
    expect(find.textContaining('2 picked'), findsOneWidget);
    await _shot(tester, '2-picking-two');

    await tester.tap(find.text('Compare'));
    await tester.pumpAndSettle();
    expect(find.text('Compare 2 APIs'), findsOneWidget);
    expect(find.text('DOCS SITE RIGHT NOW'), findsOneWidget);
    expect(find.text('DEMO_KEY'), findsOneWidget); // NASA's column
    expect(find.text('not measured'), findsWidgets);
    expect(find.text('Measure now'), findsOneWidget);
    await _shot(tester, '3-compare-top');
    await tester.drag(find.text('DEMO KEY'), const Offset(0, -700));
    await tester.pumpAndSettle();
    await _shot(tester, '4-compare-lower');

    // back to the list: still picking; the cross ends it
    await tester.pageBack();
    await tester.pumpAndSettle();
    expect(find.byType(PickBar), findsOneWidget);
    await tester.tap(find.byTooltip('Stop picking'));
    await tester.pump(const Duration(milliseconds: 300));
    expect(find.byType(PickBar), findsNothing);
  });

  testWidgets('four APIs: the table scrolls sideways and nothing overflows; a fifth pick is refused', (tester) async {
    _phone(tester);
    await tester.pumpWidget(_app(const HomePage(), brightness: Brightness.dark));
    await tester.pump(const Duration(milliseconds: 300));
    await tester.longPress(row('Cat Facts'));
    await tester.pump(const Duration(milliseconds: 300));
    for (final name in ['GitHub', 'NASA', 'Open-Meteo', 'TheCocktailDB']) {
      await tester.tap(row(name));
      await tester.pump(const Duration(milliseconds: 300));
    }
    expect(find.textContaining('4 picked'), findsOneWidget);
    expect(find.textContaining('up to 4'), findsOneWidget); // the snack bar for the fifth
    await tester.tap(find.widgetWithText(FilledButton, 'Compare'));
    await tester.pumpAndSettle();
    expect(find.text('Compare 4 APIs'), findsOneWidget);
    await _shot(tester, '5-compare-four-dark');
  });

  final history = [
    TestHistoryEntry(at: DateTime(2026, 10, 4, 9, 30), method: 'GET', ok: true, summary: '200 OK · 312 ms · application/json · 17.3 KB', url: 'https://www.thecocktaildb.com/api/json/v1/1/search.php?s=margarita', response: '{"drinks":[{"idDrink":"11007","strDrink":"Margarita","strTags":null}]}'),
    TestHistoryEntry(at: DateTime(2026, 10, 4, 9, 20), method: 'GET', ok: true, summary: '200 OK · 280 ms · application/json · 2.1 KB', url: 'https://www.thecocktaildb.com/api/json/v1/1/lookup.php?i=11007', response: '{"drinks":[{"idDrink":"11007"}]}'),
    TestHistoryEntry(at: DateTime(2026, 10, 4, 9, 10), method: 'GET', ok: false, summary: '404 Not Found · 90 ms', url: 'https://www.thecocktaildb.com/api/json/v1/1/nothing.php', response: ''),
  ];

  testWidgets('C# client: the requests that worked are ticked, the class follows the ticks', (tester) async {
    _phone(tester);
    final view = ApiView(_entry('TheCocktailDB', 'https://www.thecocktaildb.com/api.php', auth: AuthKind.apiKey), k);
    await tester.pumpWidget(_app(ClientPage(view: view, history: history, variables: const {})));
    await tester.pump();
    Finder code(String part) => find.byWidgetPredicate((w) => w is SelectableText && (w.data ?? '').contains(part));
    expect(find.byType(CheckboxListTile), findsNWidgets(2)); // the failed test is not offered
    expect(code('public sealed class TheCocktailDBClient'), findsOneWidget);
    expect(code('GetSearchAsync(string s = "margarita"'), findsOneWidget);
    expect(code('GetLookupAsync(int i = 11007'), findsOneWidget);
    expect(find.text('THECOCKTAILDBCLIENT.CS'), findsOneWidget);
    await _shot(tester, '6-client');

    await tester.tap(find.text('None'));
    await tester.pump();
    expect(find.text('Tick at least one request.'), findsOneWidget);
    await tester.tap(find.text('Tick all'));
    await tester.pump();
    expect(code('GetLookupAsync'), findsOneWidget);
  });

  testWidgets('C# classes for a response', (tester) async {
    _phone(tester);
    final classes = generateCSharp(history.first.response, rootName: 'TheCocktailDBResponse')!;
    await tester.pumpWidget(_app(CodePage(title: 'C# classes', fileName: 'TheCocktailDBResponse.cs', code: classes)));
    await tester.pump();
    expect(find.text('C# classes'), findsOneWidget);
    expect(find.byWidgetPredicate((w) => w is SelectableText && (w.data ?? '').contains('public List<Drink> Drinks { get; set; } = [];')), findsOneWidget);
    await _shot(tester, '7-classes');
  });

  testWidgets('the comparison alone, three wide', (tester) async {
    _phone(tester);
    final apis = [for (final key in ['catfact.ninja', 'api.nasa.gov', 'thecocktaildb.com/api.php']) state.find(key)!];
    await tester.pumpWidget(_app(ComparePage(apis: apis)));
    await tester.pumpAndSettle();
    expect(find.text('Compare 3 APIs'), findsOneWidget);
    expect(find.text('no known example request'), findsOneWidget); // Cat Facts
    await _shot(tester, '8-compare-three');
  });
}
