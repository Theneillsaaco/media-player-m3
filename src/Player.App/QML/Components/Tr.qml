import QtQuick

// The texts of the current language. json is what the LocaleViewModel provides: a JSON object of key -> text.
// Until it arrives, and for a key it lacks, the key itself shows. Everything else reads this through the window:
// Window.window.i18n (leaf components) and tr(key) in each component.
QtObject {
    property string json: ""
    readonly property var table: parse(json)

    function parse(text) {
        try { return text.length > 0 ? JSON.parse(text) : ({}) } catch (e) { return ({}) }
    }
    function t(key) {
        const value = table[key]
        return value !== undefined ? value : key
    }
}
