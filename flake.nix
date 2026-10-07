{
  description = "Player: YouTube Music player (.NET + Qt Bridge for C# + libmpv)";

  inputs.nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";

  outputs = { self, nixpkgs }:
    let
      system = "x86_64-linux";
      pkgs = nixpkgs.legacyPackages.${system};
      lib = pkgs.lib;
      qt = pkgs.qt6;

      # Qt Bridge wants ONE Qt prefix with lib/cmake/Qt6/Qt6Config.cmake. Nix splits Qt into
      # out/dev/bin outputs, so merge them into a single prefix and point QtDir at it.
      qtPrefix = pkgs.symlinkJoin {
        name = "qt6-prefix";
        paths = lib.concatMap (p: [ p (lib.getDev p) (lib.getBin p) ])
          ([ qt.qtbase qt.qtdeclarative qt.qtshadertools qt.qtsvg qt.qtwayland ]
            # The bridge docs list these modules as expected in a Qt installation.
            ++ map (n: qt.${n}) (lib.filter (n: qt ? ${n}) [ "qtquick3d" "qtquick3dphysics" "qtquicktimeline" ]));

        # The bridge's deploy step (cmake --install) copies every plugin type a Qt module lists in
        # its modules/<Module>.json. platformthemes pulls GTK3 -> tinysparql -> a NEEDED entry that
        # is an absolute store path, which file(GET_RUNTIME_DEPENDENCIES) rejects. This player draws
        # its own Material 3 UI, so stop listing those types. The plugin files stay in place:
        # Qt6Gui's imported targets still need them to exist at configure time.
        nativeBuildInputs = [ pkgs.jq ];
        postBuild = ''
          for f in $(find -L $out -name Gui.json -o -name Sql.json); do
            d=$(dirname "$f")
            if [ -L "$d" ]; then   # directory is a symlink into the store: make it a real one
              tgt=$(readlink -f "$d"); rm "$d"; mkdir "$d"
              for x in "$tgt"/*; do ln -s "$x" "$d/$(basename "$x")"; done
            fi
            tmp=$(mktemp)
            jq '.plugin_types |= ((. // []) - ["platformthemes", "sqldrivers"])' "$f" > "$tmp"
            rm -f "$f"; cp "$tmp" "$f"
          done

          # Deploy copies plugins and then edits their RPATH in place. Store files are read-only,
          # so give the prefix its own writable copies (file(INSTALL) keeps source permissions).
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

      # The bridge ships prebuilt native binaries; NixOS needs these on the loader path.
      runtimeLibs = with pkgs; [
        stdenv.cc.cc.lib zlib openssl icu fontconfig freetype libGL libxkbcommon
        wayland dbus glib vulkan-loader mpv-unwrapped qtPrefix
      ];
    in {
      devShells.${system}.default = pkgs.mkShell {
        # Qt's own development dependencies (OpenGL, xkbcommon, vulkan headers...). The cmake
        # setup hook turns every one of them into CMAKE_PREFIX_PATH / PKG_CONFIG_PATH entries,
        # which is what the bridge's internal cmake needs to find WrapOpenGL and friends.
        inputsFrom = [ qt.qtbase qt.qtdeclarative ];
        nativeBuildInputs = [ pkgs.cmake pkgs.ninja pkgs.pkg-config ];
        packages = [
          pkgs.dotnet-sdk_9
          (pkgs.python3.withPackages (ps: [ ps.ytmusicapi ps.yt-dlp ]))
          pkgs.deno   # JS runtime yt-dlp needs for YouTube
          pkgs.mpv    # CLI, only for debugging streams by hand
        ];
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
    };
}
