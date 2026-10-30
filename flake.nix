{
  description = "Player: YouTube Music player (.NET + Qt Bridge for C# + libmpv)";

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
      runtimeBins = [ pythonEnv pkgs.deno pkgs.mpv pkgs.xdg-utils pkgs.libsecret ];
    in {
      devShells.${system}.default = pkgs.mkShell {
        inputsFrom = [ qt.qtbase qt.qtdeclarative ];
        nativeBuildInputs = [ pkgs.cmake pkgs.ninja pkgs.pkg-config ];
        packages = [ pkgs.dotnet-sdk_10 pkgs.libsecret pkgs.flatpak pkgs.flatpak-builder ] ++ runtimeBins;
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
        pname = "player-app";
        version = "0.1.0";
        src = ./.;

        projectFile = "src/Player.App/Player.App.csproj";
        nugetDeps = ./deps.json;
        dotnet-sdk = pkgs.dotnet-sdk_10;
        dotnet-runtime = pkgs.dotnet-runtime_10;

        nativeBuildInputs = [ pkgs.stdenv.cc pkgs.cmake pkgs.ninja pkgs.pkg-config ];
        buildInputs = [ qt.qtbase qt.qtdeclarative ];
        
        dontUseCmakeConfigure = true;
        dontUseNinjaBuild = true;
        dontUseNinjaInstall = true;
        dontUseNinjaCheck = true;
        dontWrapQtApps = true;

        env = {
          PLAYER_NIX = "1";
          QtDir = "${qtPrefix}";
          CMAKE_PREFIX_PATH = "${qtPrefix}";
          QT_NO_PRIVATE_MODULE_WARNING = "ON";
        };
          
        runtimeDeps = runtimeLibs;
        makeWrapperArgs = [
          "--set" "QtDir" "${qtPrefix}"
          "--set" "QSG_RHI_BACKEND" "vulkan"
          "--set" "QT_NO_PRIVATE_MODULE_WARNING" "ON"
          "--set" "QT_PLUGIN_PATH" "${qtPrefix}/${qt.qtbase.qtPluginPrefix}"
          "--set" "QML_IMPORT_PATH" "${qtPrefix}/${qt.qtbase.qtQmlPrefix}"
          "--set" "QT_QUICK_CONTROLS_STYLE" "Basic"
          "--set" "PLAYER_SIDECAR_DIR" "${placeholder "out"}/share/player/sidecar"
          "--prefix" "PATH" ":" "${lib.makeBinPath runtimeBins}" 
        ];

        postInstall = ''
          d=src/Player.App/bin/Release/net10.0/linux-x64
          for f in player_app Application qt_bridge_metadata.json qtdeploy.txt; do
            if [ -e "$d/$f" ]; then
              cp -r "$d/$f" "$out/lib/player-app/"
            fi
          done
        
          mkdir -p $out/share/player
          cp -r sidecar $out/share/player/
          
          ${pythonEnv.interpreter} -m compileall -o 1 $out/share/player/sidecar

          mkdir -p $out/share/applications
          cat <<'EOF' > $out/share/applications/media-player.desktop
          [Desktop Entry]
          Type=Application
          Name=YouTube Music Player
          Exec=media-player
          Comment=YouTube Music player (.NET + Qt Bridge + libmpv)
          Categories=AudioVideo;Player;Music;
          Terminal=false
          StartupNotify=true
          Keywords=Music;YouTube;Player;Audio;
          EOF
        '';

        postFixup = ''
          if [ -f "$out/bin/Player.App" ]; then
          mv "$out/bin/Player.App" "$out/bin/media-player"
          fi
        '';
        
        meta.mainProgram = "media-player";
      };
    };
}