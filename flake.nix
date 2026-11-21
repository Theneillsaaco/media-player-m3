{
  description = "Kyuto: YouTube Music player (.NET + Qt Bridge for C# + libmpv)";

  inputs.nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";

  outputs = { self, nixpkgs }:
    let
      system = "x86_64-linux";
      pkgs = nixpkgs.legacyPackages.${system};
      lib = pkgs.lib;
      qt = pkgs.qt6;

      qtPrefix = pkgs.symlinkJoin {
        name = "qt6-prefix";
        paths = lib.concatMap (p: [ p (lib.getDev p) (lib.getBin p) ])
          ([ qt.qtbase qt.qtdeclarative qt.qtshadertools qt.qtsvg qt.qtwayland qt.qtmultimedia ]);
        nativeBuildInputs = [ pkgs.jq ];
        postBuild = ''
          for f in $(find -L $out -name Gui.json -o -name Sql.json); do
            d=$(dirname "$f")
            if [ -L "$d" ]; then
              tgt=$(readlink -f "$d"); rm "$d"; mkdir "$d"
              for x in "$tgt"/*; do ln -s "$x" "$d/$(basename "$x")"; done
            fi
            tmp=$(mktemp)
            jq '.plugin_types |= ((. // []) - ["platformthemes", "sqldrivers"])' "$f" > "$tmp"
            rm -f "$f"; cp "$tmp" "$f"
          done
        
          P=$out/${qt.qtbase.qtPluginPrefix}
          if [ -d "$P" ]; then
            tmpdir=$(mktemp -d)
            cp -rL --no-preserve=mode "$P"/. "$tmpdir"/
            rm -rf "$P"
            mkdir -p "$P"
            cp -r "$tmpdir"/. "$P"/
            chmod -R u+w "$P"
          fi
        '';
      };

      runtimeLibs = with pkgs; [
        stdenv.cc.cc.lib zlib openssl icu fontconfig freetype libGL libxkbcommon
        wayland dbus glib vulkan-loader mpv-unwrapped qtPrefix
      ];

      pythonEnv = pkgs.python3.withPackages (ps: [ ps.ytmusicapi ps.yt-dlp ps.secretstorage ]);
      runtimeBins = [ pythonEnv pkgs.nodejs pkgs.mpv pkgs.xdg-utils pkgs.libsecret ];
    in {
      devShells.${system}.default = pkgs.mkShell {
        inputsFrom = [ qt.qtbase qt.qtdeclarative ];
        nativeBuildInputs = [ pkgs.cmake pkgs.ninja pkgs.pkg-config ];
        packages = [ pkgs.dotnet-sdk_11 pkgs.libsecret pkgs.flatpak pkgs.flatpak-builder ] ++ runtimeBins;
        shellHook = ''
          export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
          export QtDir=${qtPrefix}
          export CMAKE_PREFIX_PATH="${qtPrefix}:''${CMAKE_PREFIX_PATH:-}"
          export QT_NO_PRIVATE_MODULE_WARNING=ON
          export LD_LIBRARY_PATH="${lib.makeLibraryPath runtimeLibs}:''${LD_LIBRARY_PATH:-}"
          export QT_PLUGIN_PATH="${qtPrefix}/${qt.qtbase.qtPluginPrefix}"
          export QML_IMPORT_PATH="${qtPrefix}/${qt.qtbase.qtQmlPrefix}"
          export QT_QUICK_CONTROLS_STYLE=Basic
        '';
      };

      packages.${system}.default = pkgs.buildDotnetModule {
        pname = "kyuto";
        version = "0.1.0";
        src = ./.;

        projectFile = "src/Kyuto.App/Kyuto.App.csproj";
        nugetDeps = ./deps.json;
        dotnet-sdk = pkgs.dotnet-sdk_11;
        dotnet-runtime = pkgs.dotnet-runtime_11;

        nativeBuildInputs = [ pkgs.stdenv.cc pkgs.cmake pkgs.ninja pkgs.pkg-config ];
        buildInputs = [ qt.qtbase qt.qtdeclarative ];
        
        dontUseCmakeConfigure = true;
        dontUseNinjaBuild = true;
        dontUseNinjaInstall = true;
        dontUseNinjaCheck = true;
        dontWrapQtApps = true;

        env = {
          KYUTO_NIX = "1";
          QtDir = "${qtPrefix}";
          CMAKE_PREFIX_PATH = "${qtPrefix}";
          QT_NO_PRIVATE_MODULE_WARNING = "ON";
        };
          
        runtimeDeps = runtimeLibs;
        makeWrapperArgs = [
          "--set" "QtDir" "${qtPrefix}"
          # "--set" "QSG_RHI_BACKEND" "vulkan"
          "--set" "QT_NO_PRIVATE_MODULE_WARNING" "ON"
          "--set" "QT_PLUGIN_PATH" "${qtPrefix}/${qt.qtbase.qtPluginPrefix}"
          "--set" "QML_IMPORT_PATH" "${qtPrefix}/${qt.qtbase.qtQmlPrefix}"
          "--set" "QT_QUICK_CONTROLS_STYLE" "Basic"
          "--set" "KYUTO_SIDECAR_DIR" "${placeholder "out"}/share/kyuto/sidecar"
          "--prefix" "PATH" ":" "${lib.makeBinPath runtimeBins}" 
        ];

        postInstall = ''
          app=$out/lib/kyuto
          qt=src/Kyuto.App/obj/Release/net11.0/qt/native
        
          # Lo que Qt Bridge necesita junto a Kyuto.App
          for d in src/Kyuto.App/bin/Release/net11.0/linux-x64 src/Kyuto.App/bin/Release/net11.0; do
            for f in Application kyuto_app qt_bridge_metadata.json qtdeploy.txt; do
              if [ -e "$d/$f" ] && [ ! -e "$app/$f" ]; then
                cp -r --no-preserve=mode "$d/$f" "$app/"
              fi
            done
          done
        
          # Respaldo: el árbol de build de Qt (el deploy con cmake --install falla en Nix y se ignora)
          [ -e "$app/Application" ] || cp -r --no-preserve=mode "$qt/build/Application" "$app/"
          [ -e "$app/kyuto_app" ] || cp --no-preserve=mode "$qt/bin/kyuto_app" "$app/"
          if [ ! -e "$app/qt_bridge_metadata.json" ]; then
            meta=$(find src/Kyuto.App -name qt_bridge_metadata.json | head -n1)
            [ -z "$meta" ] || cp --no-preserve=mode "$meta" "$app/"
          fi
        
          test -e "$app/Application" || { echo "Falta el módulo Application de Qt"; exit 1; }
        
          mkdir -p $out/share/kyuto
          cp -r sidecar $out/share/kyuto/
        
          ${pythonEnv.interpreter} -m compileall -o 1 $out/share/kyuto/sidecar
        
          mkdir -p $out/share/icons/hicolor/scalable/apps
          cp flatpak/io.github.Theneillsaaco.Kyuto.svg $out/share/icons/hicolor/scalable/apps/io.github.Theneillsaaco.Kyuto.svg
          
          mkdir -p $out/share/applications
          cat <<'EOF' > $out/share/applications/kyuto.desktop
          [Desktop Entry]
          Type=Application
          Name=Kyuto
          Exec=kyuto
          Icon=io.github.Theneillsaaco.Kyuto
          Comment=YouTube Music player (.NET + Qt Bridge + libmpv)
          Categories=AudioVideo;Audio;Player;Music;
          Terminal=false
          StartupNotify=true
          Keywords=Music;YouTube;Player;Audio;
          EOF
        '';

        postFixup = ''
          if [ -f "$out/bin/Kyuto.App" ]; then
            mv "$out/bin/Kyuto.App" "$out/bin/kyuto"
          fi
        '';
        
        meta.mainProgram = "kyuto";
      };
    };
}