import QtQuick
import uicomponent
import appUI.common.cache

AnimationImage {
    id: replay
    objectName: "KakaoAutoPlayQtPreview"
    readonly property bool kakaoAutoPlayQt: true
    property double heartbeat: Date.now()
    property int observedFrames: 0
    property int observedLoops: 0
    property int previousFrame: -1
    property var savedZ: []
    readonly property int originalStatus: originalProvider.status
    readonly property int downloadStatus: originalDownloader.status
    property string requestedOriginal: ""
    property bool autoDownload: true
    property bool downloadGranted: false
    property int downloadAttempts: 0
    property double requestStartedAt: 0
    property double retryAt: 0
    property bool downloadFailed: false
    readonly property bool downloadEligible: autoDownload && eligible && onScreen && !downloadFailed
    onDownloadGrantedChanged: {
        if (downloadGranted) downloadStart.restart();
        else { requestStartedAt = 0; originalDownloader.stop(); }
    }
    readonly property string mediaKey: String(media?.originalCachePath ?? "")
    readonly property bool album: typeof modelData !== "undefined" && Boolean(modelData?.originalCachePath)
    readonly property var media: album ? modelData : details
    readonly property string mediaFormat: (Boolean(media?.isWebp) || /webp$/i.test(media?.extension ?? "")) ? "webp" : "gif"
    property bool viewportVisible: false
    property string loadedSource: ""
    anchors.fill: parent
    sourceSize: Qt.size(Math.max(1, width), Math.max(1, height))
    fillMode: AnimationImage.PreserveAspectCrop
    cache: false
    infinite: true
    maxSourceBytes: 67108864
    readonly property bool eligible: Boolean(media) && !upload.enabled && (Boolean(media.isWebp) || Boolean(media.isGif) || Boolean(media.animated) || /\.?(gif|webp)$/i.test(media.extension ?? ""))
    readonly property bool onScreen: viewportVisible && parent.visible && Boolean(item.Window.window?.visible) && item.Window.window.visibility !== Window.Minimized
    // SourceProvider starts uncached requests only for visible targets.
    visible: eligible && onScreen
    opacity: frameCount > 1 && status === AnimationImage.Ready && loadedSource === String(media?.originalCachePath ?? "") ? 1 : 0
    playing: frameCount > 1 && eligible && onScreen && status === AnimationImage.Ready
    onCurrentFrameChanged: {
        if (previousFrame !== currentFrame) {
            observedFrames++;
            if (previousFrame > currentFrame) observedLoops++;
            previousFrame = currentFrame;
        }
    }
    SourceProvider {
        id: originalProvider
        source: replay.eligible && replay.onScreen ? (media?.originalCachePath ?? "") : ""
        downloadActive: replay.downloadGranted && replay.autoDownload && replay.eligible && replay.onScreen
        onSourceChanged: replay.loadedSource = ""
        onStatusChanged: if (status === SourceProvider.COMPLETED) replay.loadedSource = String(source)
    }
    SourceDownloader {
        id: originalDownloader
        target: SourceDownloader.CACHE_ONLY
        source: replay.eligible && replay.onScreen ? (media?.originalCachePath ?? "") : ""
        onSourceChanged: {
            replay.requestedOriginal = "";
            replay.downloadAttempts = 0;
            replay.requestStartedAt = 0;
            replay.retryAt = 0;
            replay.downloadFailed = false;
            if (String(source)) downloadStart.restart();
            else stop();
        }
        onStatusChanged: {
            if (status === SourceDownloader.COMPLETED) {
                replay.requestStartedAt = 0;
                originalProvider.reload();
            } else if (status === SourceDownloader.ERROR_OCCURRED && replay.requestStartedAt) {
                replay.requestStartedAt = 0;
                replay.downloadFailed = error === SourceDownloader.EXPIRED || replay.downloadAttempts >= 3;
                replay.retryAt = Date.now() + 5000 * replay.downloadAttempts;
            }
        }
    }
    Timer {
        id: downloadStart
        interval: 100
        onTriggered: replay.pumpDownload()
    }
    function pumpDownload() {
        const now = Date.now();
        if (requestStartedAt && now - requestStartedAt >= 60000) {
            requestStartedAt = 0;
            originalDownloader.stop();
            downloadFailed = downloadAttempts >= 3;
            retryAt = now + 5000 * downloadAttempts;
        }
        if (!downloadGranted || !autoDownload || !eligible || !onScreen || downloadFailed || requestStartedAt || now < retryAt) return;
        if (originalProvider.status === SourceProvider.COMPLETED || originalDownloader.status === SourceDownloader.COMPLETED) return;
        const key = String(originalDownloader.source);
        if (!key || (media?.originalAvailable ?? false)) return;
        requestedOriginal = key;
        downloadAttempts++;
        requestStartedAt = now;
        originalDownloader.start();
    }

    Timer {
        interval: 500
        repeat: true
        running: true
        onTriggered: {
            if (Date.now() - replay.heartbeat > 6000) { replay.destroy(); return; }
            replay.updateViewport();
            replay.pumpDownload();
        }
    }
    function updateViewport() {
        let shown = parent.visible;
        const win = item.Window.window;
        const point = parent.mapToItem(null, 0, 0);
        shown = shown && Boolean(win?.visible) && point.x + width > 0 && point.y + height > 0 && point.x < win.width && point.y < win.height;
        for (let ancestor = parent.parent, depth = 0; shown && ancestor && depth < 32; ancestor = ancestor.parent, depth++) {
            if (ancestor.clip) {
                const p = parent.mapToItem(ancestor, 0, 0);
                shown = p.x + width > 0 && p.y + height > 0 && p.x < ancestor.width && p.y < ancestor.height;
            }
        }
        viewportVisible = shown;
    }
    Component.onCompleted: {
        updateViewport();
        downloadStart.restart();
        const entries = [];
        for (const child of parent.children) {
            if (child !== replay) {
                entries.push({object: child, z: child.z, assigned: Math.max(child.z, 1)});
                child.z = Math.max(child.z, 1);
            }
        }
        savedZ = entries;
    }
    Component.onDestruction: {
        originalDownloader.stop();
        for (const entry of savedZ)
            if (Qt.isQtObject(entry.object) && entry.object.z === entry.assigned) entry.object.z = entry.z;
    }
}
