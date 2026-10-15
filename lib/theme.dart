import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

/// The ledger look: one ink, one hairline, one accent (the teal of the icon), and colour only where it means something.
ThemeData ledgerTheme(Brightness brightness) {
  final dark = brightness == Brightness.dark;
  final ink = dark ? const Color(0xFFE8ECF1) : const Color(0xFF111418);
  final ground = dark ? const Color(0xFF0F1114) : Colors.white;
  final line = dark ? const Color(0xFF262B33) : const Color(0xFFE3E7EC);
  final muted = dark ? const Color(0xFF98A2B0) : const Color(0xFF6B7480);
  final tint = dark ? const Color(0xFF181C22) : const Color(0xFFF3F5F7);
  final accent = dark ? const Color(0xFF3ECDB8) : const Color(0xFF158F82);
  final scheme = ColorScheme.fromSeed(seedColor: accent, brightness: brightness).copyWith(
    primary: accent,
    onPrimary: dark ? const Color(0xFF0F1114) : Colors.white,
    surface: ground,
    onSurface: ink,
    onSurfaceVariant: muted,
    outline: muted,
    outlineVariant: line,
    surfaceContainerLowest: ground,
    surfaceContainerLow: ground,
    surfaceContainer: ground,
    surfaceContainerHigh: tint,
    surfaceContainerHighest: tint,
    secondaryContainer: tint,
    onSecondaryContainer: ink,
    surfaceTint: Colors.transparent,
  );
  final overlay = SystemUiOverlayStyle(
    statusBarColor: Colors.transparent,
    statusBarIconBrightness: dark ? Brightness.light : Brightness.dark,
    systemNavigationBarColor: Colors.transparent,
    systemNavigationBarIconBrightness: dark ? Brightness.light : Brightness.dark,
    systemNavigationBarContrastEnforced: false,
  );
  const bold = TextStyle(fontFamily: 'Manrope', fontWeight: FontWeight.w700);
  return ThemeData(
    useMaterial3: true,
    colorScheme: scheme,
    fontFamily: 'Manrope',
    scaffoldBackgroundColor: ground,
    splashFactory: InkSparkle.splashFactory,
    appBarTheme: AppBarTheme(
        backgroundColor: ground,
        foregroundColor: ink,
        elevation: 0,
        scrolledUnderElevation: 0,
        surfaceTintColor: Colors.transparent,
        systemOverlayStyle: overlay,
        // every page title in the ledger's heavy weight, whether or not the page styles it itself
        titleTextStyle: TextStyle(fontFamily: 'Manrope', fontSize: 17, fontWeight: FontWeight.w800, letterSpacing: -0.2, color: ink)),
    dividerTheme: DividerThemeData(color: line, thickness: 1, space: 1),
    cardTheme: CardThemeData(color: ground, elevation: 0, surfaceTintColor: Colors.transparent, shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(6), side: BorderSide(color: line))),
    filledButtonTheme: FilledButtonThemeData(style: FilledButton.styleFrom(backgroundColor: ink, foregroundColor: ground, shape: const StadiumBorder(), textStyle: bold)),
    outlinedButtonTheme: OutlinedButtonThemeData(style: OutlinedButton.styleFrom(foregroundColor: ink, side: BorderSide(color: ink, width: 1.5), shape: const StadiumBorder(), textStyle: bold)),
    textButtonTheme: TextButtonThemeData(style: TextButton.styleFrom(foregroundColor: accent, textStyle: bold)),
    chipTheme: ChipThemeData(
      backgroundColor: ground,
      selectedColor: ink,
      checkmarkColor: ground,
      side: BorderSide(color: line),
      shape: const StadiumBorder(),
      labelStyle: TextStyle(fontFamily: 'Manrope', fontWeight: FontWeight.w600, fontSize: 12.5, color: WidgetStateColor.resolveWith((s) => s.contains(WidgetState.selected) ? ground : ink)),
    ),
    inputDecorationTheme: InputDecorationTheme(
      isDense: true,
      border: OutlineInputBorder(borderRadius: BorderRadius.circular(4), borderSide: BorderSide(color: line)),
      enabledBorder: OutlineInputBorder(borderRadius: BorderRadius.circular(4), borderSide: BorderSide(color: line)),
      focusedBorder: OutlineInputBorder(borderRadius: BorderRadius.circular(4), borderSide: BorderSide(color: ink, width: 1.5)),
      labelStyle: TextStyle(color: muted),
      helperStyle: TextStyle(color: muted, fontSize: 11),
    ),
    snackBarTheme: SnackBarThemeData(behavior: SnackBarBehavior.floating, backgroundColor: ink, contentTextStyle: TextStyle(fontFamily: 'Manrope', color: ground, fontWeight: FontWeight.w600)),
    tabBarTheme: TabBarThemeData(labelColor: ink, unselectedLabelColor: muted, indicatorColor: ink, indicatorSize: TabBarIndicatorSize.label, dividerColor: line, labelStyle: bold.copyWith(fontSize: 13), unselectedLabelStyle: bold.copyWith(fontSize: 13, fontWeight: FontWeight.w600)),
    listTileTheme: ListTileThemeData(selectedColor: ink, selectedTileColor: tint),
    drawerTheme: DrawerThemeData(backgroundColor: ground, surfaceTintColor: Colors.transparent, shape: const RoundedRectangleBorder()),
    // the ledger's small radii and heavy titles, instead of Material's 28 dp pillows
    bottomSheetTheme: BottomSheetThemeData(backgroundColor: ground, surfaceTintColor: Colors.transparent, shape: const RoundedRectangleBorder(borderRadius: BorderRadius.vertical(top: Radius.circular(12)))),
    dialogTheme: DialogThemeData(
        backgroundColor: ground,
        surfaceTintColor: Colors.transparent,
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(8), side: BorderSide(color: line)),
        titleTextStyle: TextStyle(fontFamily: 'Manrope', fontSize: 20, fontWeight: FontWeight.w800, letterSpacing: -0.3, color: ink),
        contentTextStyle: TextStyle(fontFamily: 'Manrope', fontSize: 14, height: 1.45, color: ink)),
    popupMenuTheme: PopupMenuThemeData(color: ground, surfaceTintColor: Colors.transparent, shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(6), side: BorderSide(color: line))),
    progressIndicatorTheme: ProgressIndicatorThemeData(color: accent, linearTrackColor: tint),
    switchTheme: SwitchThemeData(thumbColor: WidgetStateProperty.resolveWith((s) => s.contains(WidgetState.selected) ? ground : muted), trackColor: WidgetStateProperty.resolveWith((s) => s.contains(WidgetState.selected) ? ink : tint)),
  );
}
