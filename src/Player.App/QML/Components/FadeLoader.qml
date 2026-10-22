import QtQuick

// A Loader that fades the old content out, swaps it and fades the new one in whenever sourceComp changes
// (after the one of Caelestia). Use sourceComp instead of sourceComponent.
Loader {
    id: root

    property Component sourceComp
    property bool isComplete
    property int outAnimType: Anim.FastEffects
    property int inAnimType: Anim.DefaultEffects

    Component.onCompleted: {
        isComplete = true
        sourceComponent = sourceComp
    }
    onSourceCompChanged: {
        if (isComplete)
            anim.restart()
    }

    SequentialAnimation {
        id: anim

        running: false

        Anim {
            target: root
            property: "opacity"
            to: 0
            type: root.outAnimType
        }
        ScriptAction {
            script: root.sourceComponent = root.sourceComp
        }
        Anim {
            target: root
            property: "opacity"
            to: 1
            type: root.inAnimType
        }
    }
}
