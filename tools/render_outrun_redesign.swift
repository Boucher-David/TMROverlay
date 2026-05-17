import AppKit
import Foundation

private enum Theme {
    static let bgTop = NSColor(red255: 18, green: 5, blue: 31)
    static let bgMid = NSColor(red255: 12, green: 18, blue: 42)
    static let bgBottom = NSColor(red255: 3, green: 11, blue: 24)
    static let panel = NSColor(red255: 7, green: 14, blue: 27, alpha: 0.96)
    static let panelAlt = NSColor(red255: 13, green: 22, blue: 43, alpha: 0.96)
    static let panelRaised = NSColor(red255: 17, green: 28, blue: 55, alpha: 0.98)
    static let titleBar = NSColor(red255: 11, green: 14, blue: 33, alpha: 0.98)
    static let border = NSColor(red255: 40, green: 72, blue: 108, alpha: 0.92)
    static let borderDim = NSColor(red255: 30, green: 52, blue: 82, alpha: 0.78)
    static let text = NSColor(red255: 255, green: 247, blue: 255)
    static let secondary = NSColor(red255: 208, green: 230, blue: 255)
    static let muted = NSColor(red255: 140, green: 174, blue: 212)
    static let dim = NSColor(red255: 82, green: 112, blue: 148)
    static let cyan = NSColor(red255: 0, green: 232, blue: 255)
    static let cyanDim = NSColor(red255: 31, green: 114, blue: 143)
    static let magenta = NSColor(red255: 255, green: 42, blue: 167)
    static let magentaDim = NSColor(red255: 106, green: 31, blue: 95)
    static let violet = NSColor(red255: 141, green: 92, blue: 255)
    static let amber = NSColor(red255: 255, green: 209, blue: 91)
    static let green = NSColor(red255: 98, green: 255, blue: 159)
    static let red = NSColor(red255: 255, green: 76, blue: 92)
    static let orange = NSColor(red255: 255, green: 125, blue: 73)
    static let purple = NSColor(red255: 126, green: 50, blue: 255)

    static func font(_ size: CGFloat, weight: NSFont.Weight = .regular, mono: Bool = false) -> NSFont {
        if mono {
            return NSFont.monospacedSystemFont(ofSize: size, weight: weight)
        }

        return NSFont(name: "SF Pro", size: size) ?? NSFont.systemFont(ofSize: size, weight: weight)
    }
}

private extension NSColor {
    convenience init(red255 red: CGFloat, green: CGFloat, blue: CGFloat, alpha: CGFloat = 1.0) {
        self.init(calibratedRed: red / 255.0, green: green / 255.0, blue: blue / 255.0, alpha: alpha)
    }
}

private final class Canvas {
    let size: CGSize

    init(size: CGSize) {
        self.size = size
    }

    func rect(_ x: CGFloat, _ y: CGFloat, _ width: CGFloat, _ height: CGFloat) -> CGRect {
        CGRect(x: x, y: size.height - y - height, width: width, height: height)
    }

    func point(_ x: CGFloat, _ y: CGFloat) -> CGPoint {
        CGPoint(x: x, y: size.height - y)
    }

    func fill(_ x: CGFloat, _ y: CGFloat, _ width: CGFloat, _ height: CGFloat, _ color: NSColor, radius: CGFloat = 0) {
        color.setFill()
        path(rect(x, y, width, height), radius).fill()
    }

    func stroke(_ x: CGFloat, _ y: CGFloat, _ width: CGFloat, _ height: CGFloat, _ color: NSColor, lineWidth: CGFloat = 1, radius: CGFloat = 0) {
        color.setStroke()
        let p = path(rect(x, y, width, height).insetBy(dx: lineWidth / 2, dy: lineWidth / 2), radius)
        p.lineWidth = lineWidth
        p.stroke()
    }

    func gradient(_ x: CGFloat, _ y: CGFloat, _ width: CGFloat, _ height: CGFloat, colors: [NSColor], angle: CGFloat = 90, radius: CGFloat = 0) {
        guard let gradient = NSGradient(colors: colors) else { return }
        let r = rect(x, y, width, height)
        if radius > 0 {
            NSGraphicsContext.saveGraphicsState()
            path(r, radius).addClip()
            gradient.draw(in: r, angle: angle)
            NSGraphicsContext.restoreGraphicsState()
        } else {
            gradient.draw(in: r, angle: angle)
        }
    }

    func line(_ x1: CGFloat, _ y1: CGFloat, _ x2: CGFloat, _ y2: CGFloat, _ color: NSColor, width: CGFloat = 1) {
        color.setStroke()
        let p = NSBezierPath()
        p.move(to: point(x1, y1))
        p.line(to: point(x2, y2))
        p.lineWidth = width
        p.stroke()
    }

    func polyline(_ points: [CGPoint], color: NSColor, width: CGFloat = 2) {
        guard let first = points.first else { return }
        color.setStroke()
        let p = NSBezierPath()
        p.move(to: point(first.x, first.y))
        for item in points.dropFirst() {
            p.line(to: point(item.x, item.y))
        }
        p.lineWidth = width
        p.stroke()
    }

    func text(
        _ value: String,
        _ x: CGFloat,
        _ y: CGFloat,
        _ width: CGFloat,
        _ height: CGFloat,
        size: CGFloat,
        weight: NSFont.Weight = .regular,
        color: NSColor = Theme.text,
        align: NSTextAlignment = .left,
        mono: Bool = false
    ) {
        let paragraph = NSMutableParagraphStyle()
        paragraph.alignment = align
        paragraph.lineBreakMode = .byTruncatingTail
        let attrs: [NSAttributedString.Key: Any] = [
            .font: Theme.font(size, weight: weight, mono: mono),
            .foregroundColor: color,
            .paragraphStyle: paragraph
        ]
        NSString(string: value).draw(in: rect(x, y, width, height), withAttributes: attrs)
    }

    func multiline(
        _ value: String,
        _ x: CGFloat,
        _ y: CGFloat,
        _ width: CGFloat,
        _ height: CGFloat,
        size: CGFloat,
        weight: NSFont.Weight = .regular,
        color: NSColor = Theme.secondary,
        align: NSTextAlignment = .left
    ) {
        let paragraph = NSMutableParagraphStyle()
        paragraph.alignment = align
        paragraph.lineSpacing = 3
        paragraph.lineBreakMode = .byWordWrapping
        let attrs: [NSAttributedString.Key: Any] = [
            .font: Theme.font(size, weight: weight),
            .foregroundColor: color,
            .paragraphStyle: paragraph
        ]
        NSString(string: value).draw(
            with: rect(x, y, width, height),
            options: [.usesLineFragmentOrigin, .usesFontLeading],
            attributes: attrs
        )
    }

    func centered(
        _ value: String,
        _ x: CGFloat,
        _ y: CGFloat,
        _ width: CGFloat,
        _ height: CGFloat,
        size: CGFloat,
        weight: NSFont.Weight = .semibold,
        color: NSColor = Theme.text,
        mono: Bool = false
    ) {
        let textHeight = size * 1.45
        text(
            value,
            x,
            y + max(0, (height - textHeight) / 2),
            width,
            textHeight,
            size: size,
            weight: weight,
            color: color,
            align: .center,
            mono: mono
        )
    }

    func pill(_ value: String, _ x: CGFloat, _ y: CGFloat, _ width: CGFloat, _ height: CGFloat, fill color: NSColor, textColor: NSColor = Theme.text) {
        fill(x, y, width, height, color, radius: height / 2)
        stroke(x, y, width, height, NSColor.white.withAlphaComponent(0.16), lineWidth: 1, radius: height / 2)
        centered(value, x + 8, y, width - 16, height, size: min(12, height * 0.45), weight: .bold, color: textColor)
    }

    func toggle(_ x: CGFloat, _ y: CGFloat, on: Bool) {
        fill(x, y, 56, 28, on ? NSColor(red255: 5, green: 60, blue: 69) : Theme.panelRaised, radius: 14)
        stroke(x, y, 56, 28, on ? Theme.cyan : Theme.border, lineWidth: 1, radius: 14)
        circle(x + (on ? 40 : 16), y + 14, radius: 10, color: on ? Theme.green : Theme.dim, fill: true)
    }

    func slider(_ x: CGFloat, _ y: CGFloat, width: CGFloat, value: CGFloat, color: NSColor = Theme.magenta) {
        fill(x, y + 11, width, 6, Theme.panelRaised, radius: 3)
        fill(x, y + 11, width * min(max(value, 0), 1), 6, color, radius: 3)
        circle(x + width * min(max(value, 0), 1), y + 14, radius: 8, color: Theme.amber, fill: true)
    }

    func checkbox(_ x: CGFloat, _ y: CGFloat, checked: Bool, label: String) {
        fill(x, y, 20, 20, checked ? NSColor(red255: 6, green: 46, blue: 55) : Theme.panelRaised, radius: 5)
        stroke(x, y, 20, 20, checked ? Theme.cyan : Theme.border, lineWidth: 1, radius: 5)
        if checked {
            line(x + 5, y + 10, x + 9, y + 15, Theme.green, width: 2)
            line(x + 9, y + 15, x + 16, y + 6, Theme.green, width: 2)
        }
        text(label, x + 28, y + 2, 130, 18, size: 12, weight: .semibold, color: checked ? Theme.secondary : Theme.muted)
    }

    func circle(_ x: CGFloat, _ y: CGFloat, radius: CGFloat, color: NSColor, fill: Bool = false, width: CGFloat = 1) {
        let r = rect(x - radius, y - radius, radius * 2, radius * 2)
        if fill {
            color.setFill()
            NSBezierPath(ovalIn: r).fill()
        } else {
            color.setStroke()
            let p = NSBezierPath(ovalIn: r)
            p.lineWidth = width
            p.stroke()
        }
    }

    func image(_ image: NSImage, inTopRect r: CGRect) {
        image.draw(in: rect(r.minX, r.minY, r.width, r.height), from: .zero, operation: .sourceOver, fraction: 1)
    }

    func imageAspectFit(_ sourceImage: NSImage, inTopRect r: CGRect) {
        let scale = min(r.width / sourceImage.size.width, r.height / sourceImage.size.height)
        let width = sourceImage.size.width * scale
        let height = sourceImage.size.height * scale
        let x = r.minX + (r.width - width) / 2
        let y = r.minY + (r.height - height) / 2
        image(sourceImage, inTopRect: CGRect(x: x, y: y, width: width, height: height))
    }

    func clipRect(_ x: CGFloat, _ y: CGFloat, _ width: CGFloat, _ height: CGFloat, radius: CGFloat = 0, _ body: () -> Void) {
        NSGraphicsContext.saveGraphicsState()
        path(rect(x, y, width, height), radius).addClip()
        body()
        NSGraphicsContext.restoreGraphicsState()
    }

    private func path(_ rect: CGRect, _ radius: CGFloat) -> NSBezierPath {
        radius <= 0 ? NSBezierPath(rect: rect) : NSBezierPath(roundedRect: rect, xRadius: radius, yRadius: radius)
    }
}

private enum OverlayKind {
    case standings
    case relative
    case gap
    case trackMap
    case streamChat
    case garageCover
    case fuel
    case inputs
    case radar
    case flags
    case sessionWeather
    case pitService
}

private struct OverlaySpec {
    let id: String
    let displayName: String
    let subtitle: String
    let defaultSize: CGSize
    let route: String?
    let showOpacity: Bool
    let showSessionFilters: Bool
    let options: [String]
    let kind: OverlayKind
}

private let overlaySpecs: [OverlaySpec] = [
    OverlaySpec(
        id: "standings",
        displayName: "Standings",
        subtitle: "Scoring-first live race table with multiclass context.",
        defaultSize: CGSize(width: 620, height: 340),
        route: "/overlays/standings",
        showOpacity: true,
        showSessionFilters: true,
        options: ["Other-class rows: 2", "Header status: Live session", "Footer source: Quiet when healthy"],
        kind: .standings
    ),
    OverlaySpec(
        id: "relative",
        displayName: "Relative",
        subtitle: "Nearby-car timing around the local in-car reference.",
        defaultSize: CGSize(width: 520, height: 360),
        route: "/overlays/relative",
        showOpacity: true,
        showSessionFilters: true,
        options: ["Cars ahead: 5", "Cars behind: 5", "Pit-road rows de-emphasized"],
        kind: .relative
    ),
    OverlaySpec(
        id: "gap-to-leader",
        displayName: "Gap To Leader",
        subtitle: "Rolling in-class gap graph for endurance trend reading.",
        defaultSize: CGSize(width: 560, height: 260),
        route: "/overlays/gap-to-leader",
        showOpacity: true,
        showSessionFilters: false,
        options: ["Cars ahead: 5", "Cars behind: 5", "Race sessions only"],
        kind: .gap
    ),
    OverlaySpec(
        id: "track-map",
        displayName: "Track Map",
        subtitle: "Transparent local map with live car dots and sector highlights.",
        defaultSize: CGSize(width: 360, height: 360),
        route: "/overlays/track-map",
        showOpacity: true,
        showSessionFilters: true,
        options: ["Map fill opacity: 42%", "Focused car label: P12", "Sector color: personal best"],
        kind: .trackMap
    ),
    OverlaySpec(
        id: "stream-chat",
        displayName: "Stream Chat",
        subtitle: "Streamer chat panel with provider-specific connection setup.",
        defaultSize: CGSize(width: 380, height: 520),
        route: "/overlays/stream-chat",
        showOpacity: false,
        showSessionFilters: false,
        options: ["Provider: Twitch channel", "Channel: techmatesracing", "Connection: waiting for chat"],
        kind: .streamChat
    ),
    OverlaySpec(
        id: "garage-cover",
        displayName: "Garage Cover",
        subtitle: "Fails-closed OBS cover for garage and setup screens.",
        defaultSize: CGSize(width: 1280, height: 720),
        route: "/overlays/garage-cover",
        showOpacity: false,
        showSessionFilters: false,
        options: ["Image: imported team cover", "Detection: garage visible", "Fit: crop to cover"],
        kind: .garageCover
    ),
    OverlaySpec(
        id: "fuel-calculator",
        displayName: "Fuel Calculator",
        subtitle: "Live burn, stint planning, and stop strategy guidance.",
        defaultSize: CGSize(width: 600, height: 320),
        route: "/overlays/fuel-calculator",
        showOpacity: true,
        showSessionFilters: true,
        options: ["Footer source by session", "Units: metric"],
        kind: .fuel
    ),
    OverlaySpec(
        id: "input-state",
        displayName: "Inputs",
        subtitle: "Throttle, brake, steering, gear, and car-state trace.",
        defaultSize: CGSize(width: 520, height: 220),
        route: "/overlays/inputs",
        showOpacity: true,
        showSessionFilters: true,
        options: ["Throttle/brake bars", "ABS/TC status", "Shift and speed readout"],
        kind: .inputs
    ),
    OverlaySpec(
        id: "car-radar",
        displayName: "Car Radar",
        subtitle: "Local in-car proximity radar and multiclass warning arc.",
        defaultSize: CGSize(width: 300, height: 300),
        route: "/overlays/radar",
        showOpacity: false,
        showSessionFilters: true,
        options: ["Show multiclass warning", "Local player focus only", "Fade when no traffic"],
        kind: .radar
    ),
    OverlaySpec(
        id: "flags",
        displayName: "Flags",
        subtitle: "Compact live session flag strip for race-control state.",
        defaultSize: CGSize(width: 360, height: 170),
        route: nil,
        showOpacity: false,
        showSessionFilters: false,
        options: ["Icon-only compact mode", "Custom size follows scale", "Hides on plain green running"],
        kind: .flags
    ),
    OverlaySpec(
        id: "session-weather",
        displayName: "Session / Weather",
        subtitle: "Session clock, track context, and weather state.",
        defaultSize: CGSize(width: 420, height: 270),
        route: "/overlays/session-weather",
        showOpacity: true,
        showSessionFilters: true,
        options: ["Session clock", "Track temperature", "Wetness and wind summary"],
        kind: .sessionWeather
    ),
    OverlaySpec(
        id: "pit-service",
        displayName: "Pit Service",
        subtitle: "Pit request, service status, tire/fuel command summary.",
        defaultSize: CGSize(width: 420, height: 250),
        route: "/overlays/pit-service",
        showOpacity: true,
        showSessionFilters: true,
        options: ["Pit command status", "Fuel add and tires", "Release-ready signal"],
        kind: .pitService
    )
]

private let settingTabOrder: [(id: String, title: String)] = [("general", "General")]
    + overlaySpecs.map { ($0.id, $0.displayName) }
    + [("support", "Support")]

private let outputRoot = URL(fileURLWithPath: "mocks/application-redesign", isDirectory: true)
private let settingsRoot = outputRoot.appendingPathComponent("settings-tabs", isDirectory: true)
private let overlaysRoot = outputRoot.appendingPathComponent("overlays", isDirectory: true)

try FileManager.default.createDirectory(at: settingsRoot, withIntermediateDirectories: true)
try FileManager.default.createDirectory(at: overlaysRoot, withIntermediateDirectories: true)

var settingsImages: [(id: String, title: String, image: NSImage)] = []
for tab in settingTabOrder {
    let image = renderSettingsScreenshot(activeTabId: tab.id)
    try writePNG(image, to: settingsRoot.appendingPathComponent("\(tab.id).png"))
    settingsImages.append((tab.id, tab.title, image))
}

var overlayImages: [(id: String, title: String, image: NSImage)] = []
for spec in overlaySpecs {
    let image = renderOverlayScreenshot(spec)
    try writePNG(image, to: overlaysRoot.appendingPathComponent("\(spec.id).png"))
    overlayImages.append((spec.id, spec.displayName, image))
}

try writePNG(
    renderContactSheet(
        title: "Outrun Settings Tab Coverage",
        subtitle: "Static concept screenshots for every current Settings UI tab.",
        items: settingsImages,
        columns: 3,
        thumbSize: CGSize(width: 900, height: 494)
    ),
    to: outputRoot.appendingPathComponent("outrun-settings-tabs-contact-sheet.png")
)

try writePNG(
    renderContactSheet(
        title: "Outrun Overlay Coverage",
        subtitle: "Static concept screenshots for every managed overlay surface.",
        items: overlayImages,
        columns: 3,
        thumbSize: CGSize(width: 780, height: 460)
    ),
    to: outputRoot.appendingPathComponent("outrun-overlays-contact-sheet.png")
)

print("Wrote \(settingsImages.count) settings screenshots and \(overlayImages.count) overlay screenshots to \(outputRoot.path).")

private func renderSettingsScreenshot(activeTabId: String) -> NSImage {
    renderImage(size: CGSize(width: 1240, height: 680)) { c in
        drawOutrunBackdrop(c)
        drawSettingsWindow(c, activeTabId: activeTabId)
    }
}

private func drawSettingsWindow(_ c: Canvas, activeTabId: String) {
    c.fill(44, 36, 1152, 608, NSColor.black.withAlphaComponent(0.28), radius: 18)
    c.gradient(44, 36, 1152, 608, colors: [NSColor(red255: 8, green: 10, blue: 23), NSColor(red255: 15, green: 9, blue: 32), NSColor(red255: 5, green: 20, blue: 37)], angle: -25, radius: 18)
    c.stroke(44, 36, 1152, 608, Theme.cyan.withAlphaComponent(0.78), lineWidth: 1.4, radius: 18)

    c.fill(44, 36, 1152, 58, Theme.titleBar, radius: 18)
    c.fill(44, 92, 1152, 2, Theme.magenta)
    c.fill(44, 94, 1152, 1, Theme.cyan)
    c.fill(66, 54, 46, 24, NSColor(red255: 17, green: 26, blue: 53), radius: 5)
    c.stroke(66, 54, 46, 24, Theme.magenta, lineWidth: 1.2, radius: 5)
    c.centered("TMR", 66, 53, 46, 24, size: 14, weight: .black)
    c.text("Tech Mates Racing Overlay", 128, 52, 480, 28, size: 24, weight: .heavy)
    c.text("Settings control plane - outrun concept skin", 129, 75, 480, 16, size: 12, color: Theme.muted)
    c.pill("LIVE", 1052, 55, 68, 22, fill: NSColor(red255: 12, green: 55, blue: 70), textColor: Theme.cyan)
    c.centered("X", 1132, 54, 30, 24, size: 13, weight: .black, color: NSColor(red255: 255, green: 200, blue: 239))

    drawSettingsSidebar(c, activeTabId: activeTabId)
    drawSettingsContent(c, activeTabId: activeTabId)
}

private func drawSettingsSidebar(_ c: Canvas, activeTabId: String) {
    c.fill(64, 116, 190, 506, NSColor(red255: 6, green: 13, blue: 26, alpha: 0.92), radius: 14)
    c.stroke(64, 116, 190, 506, Theme.borderDim, lineWidth: 1, radius: 14)
    c.text("SETTINGS", 84, 136, 110, 18, size: 12, weight: .heavy, color: Theme.cyan)

    let tabHeight: CGFloat = 27
    let gap: CGFloat = 5
    let startY: CGFloat = 164
    for (index, tab) in settingTabOrder.enumerated() {
        let y = startY + CGFloat(index) * (tabHeight + gap)
        let active = tab.id == activeTabId
        c.fill(78, y, 162, tabHeight, active ? NSColor(red255: 48, green: 16, blue: 68) : NSColor(red255: 17, green: 26, blue: 50), radius: 8)
        if active {
            c.stroke(78, y, 162, tabHeight, Theme.magenta, lineWidth: 1.3, radius: 8)
            c.fill(78, y, 5, tabHeight, Theme.cyan, radius: 3)
        }
        c.text(tab.title, 92, y + 7, 132, 16, size: 11.5, weight: active ? .heavy : .semibold, color: active ? Theme.text : NSColor(red255: 185, green: 217, blue: 255))
    }
}

private func drawSettingsContent(_ c: Canvas, activeTabId: String) {
    c.fill(278, 116, 890, 506, NSColor(red255: 8, green: 17, blue: 33, alpha: 0.94), radius: 16)
    c.stroke(278, 116, 890, 506, Theme.border, lineWidth: 1.2, radius: 16)

    if activeTabId == "general" {
        drawGeneralSettings(c)
    } else if activeTabId == "support" {
        drawSupportSettings(c)
    } else if let spec = overlaySpecs.first(where: { $0.id == activeTabId }) {
        drawOverlaySettings(c, spec: spec)
    }
}

private func drawGeneralSettings(_ c: Canvas) {
    drawContentHeader(c, title: "General", subtitle: "Shared app preferences that apply across overlay surfaces.", status: "APP")
    drawPanelHeader(c, x: 306, y: 214, width: 392, height: 156, title: "Units")
    c.text("Measurement system", 328, 278, 180, 18, size: 13, color: Theme.secondary)
    c.fill(506, 264, 154, 34, Theme.panelRaised, radius: 17)
    c.fill(512, 270, 70, 22, Theme.magenta, radius: 11)
    c.centered("Metric", 512, 269, 70, 22, size: 11, weight: .bold)
    c.centered("Imperial", 586, 269, 70, 22, size: 11, weight: .bold, color: Theme.muted)
    c.multiline("Font selection stays in theme/platform work for this pass so preview screenshots remain stable across macOS and Windows.", 328, 316, 322, 42, size: 12, color: Theme.muted)

    drawPanelHeader(c, x: 726, y: 214, width: 414, height: 156, title: "Localhost Browser Sources")
    c.pill("ENABLED", 1040, 232, 74, 22, fill: NSColor(red255: 6, green: 50, blue: 43), textColor: Theme.green)
    c.text("Port", 750, 276, 80, 18, size: 12, color: Theme.muted)
    c.text("5011", 846, 276, 80, 18, size: 14, weight: .bold, color: Theme.text, mono: true)
    c.text("Routes", 750, 312, 80, 18, size: 12, color: Theme.muted)
    c.text("11 OBS pages active", 846, 312, 180, 18, size: 14, weight: .bold, color: Theme.secondary)

    drawPanelHeader(c, x: 306, y: 396, width: 834, height: 170, title: "Startup Surface")
    c.text("Settings opens first", 328, 458, 210, 20, size: 15, weight: .bold)
    c.multiline("Driving overlays stay hidden until enabled here. This concept keeps the production control surface fixed-size and direct, with brighter theme tokens layered on top of the current workflow.", 328, 488, 430, 52, size: 13, color: Theme.secondary)
    c.fill(806, 438, 288, 82, NSColor(red255: 5, green: 9, blue: 20), radius: 10)
    c.stroke(806, 438, 288, 82, Theme.cyan.withAlphaComponent(0.75), lineWidth: 1, radius: 10)
    c.text("DEFAULT WINDOW", 828, 462, 120, 18, size: 11, weight: .heavy, color: Theme.cyan)
    c.text("1240 x 680", 828, 486, 160, 24, size: 22, weight: .heavy, color: Theme.text, mono: true)
    c.pill("NORMAL DESKTOP", 972, 468, 100, 24, fill: NSColor(red255: 35, green: 17, blue: 49), textColor: Theme.magenta)
}

private func drawSupportSettings(_ c: Canvas) {
    drawContentHeader(c, title: "Support", subtitle: "Diagnostics and teammate handoff controls stay task-oriented.", status: "READY")
    drawPanelHeader(c, x: 306, y: 214, width: 392, height: 170, title: "Diagnostic Capture")
    c.toggle(328, 274, on: true)
    c.text("Raw diagnostic telemetry", 400, 278, 220, 18, size: 14, weight: .bold)
    c.multiline("Capture writes raw frames only when explicitly requested. Live overlay diagnostics remain lightweight by default.", 328, 318, 318, 46, size: 12, color: Theme.muted)

    drawPanelHeader(c, x: 726, y: 214, width: 414, height: 170, title: "Current State")
    statusRow(c, x: 750, y: 270, label: "iRacing", value: "Connected", color: Theme.green)
    statusRow(c, x: 750, y: 304, label: "Session", value: "Race - Nurburgring", color: Theme.cyan)
    statusRow(c, x: 750, y: 338, label: "Issue", value: "No active warnings", color: Theme.green)

    drawPanelHeader(c, x: 306, y: 410, width: 834, height: 156, title: "Support Bundle")
    c.text("Latest bundle", 328, 474, 120, 18, size: 12, color: Theme.muted)
    c.text("tmroverlay-diagnostics-20260507-1442.zip", 456, 474, 330, 18, size: 13, weight: .bold, color: Theme.secondary, mono: true)
    c.fill(328, 510, 138, 34, NSColor(red255: 39, green: 15, blue: 55), radius: 8)
    c.stroke(328, 510, 138, 34, Theme.magenta, lineWidth: 1, radius: 8)
    c.centered("Create Bundle", 328, 509, 138, 34, size: 12, weight: .heavy)
    c.fill(482, 510, 120, 34, NSColor(red255: 8, green: 42, blue: 54), radius: 8)
    c.stroke(482, 510, 120, 34, Theme.cyan, lineWidth: 1, radius: 8)
    c.centered("Open Logs", 482, 509, 120, 34, size: 12, weight: .heavy)
    c.text("Storage shortcuts and release update state sit here, not inside normal overlay tabs.", 642, 518, 390, 18, size: 12, color: Theme.muted)
}

private func drawOverlaySettings(_ c: Canvas, spec: OverlaySpec) {
    drawContentHeader(c, title: spec.displayName, subtitle: spec.subtitle, status: "100%")

    c.fill(306, 202, 282, 42, NSColor(red255: 8, green: 15, blue: 31), radius: 21)
    c.stroke(306, 202, 282, 42, Theme.borderDim, lineWidth: 1, radius: 21)
    c.fill(312, 208, 86, 30, Theme.magenta, radius: 15)
    c.centered("General", 312, 207, 86, 30, size: 12, weight: .heavy)
    c.centered("Header", 410, 207, 76, 30, size: 12, weight: .heavy, color: Theme.cyan)
    c.centered("Footer", 496, 207, 76, 30, size: 12, weight: .heavy, color: Theme.cyan)

    drawPanelHeader(c, x: 306, y: 272, width: 392, height: 226, title: "Overlay Controls")
    c.text("Visible", 328, 334, 100, 18, size: 13, color: Theme.secondary)
    c.toggle(600, 328, on: true)
    c.text("Scale", 328, 374, 100, 18, size: 13, color: Theme.secondary)
    c.slider(454, 368, width: 180, value: 0.54, color: Theme.cyan)
    c.text("100%", 642, 371, 40, 18, size: 12, weight: .bold, color: Theme.text, align: .right)
    if spec.showOpacity {
        c.text("Opacity", 328, 414, 100, 18, size: 13, color: Theme.secondary)
        c.slider(454, 408, width: 180, value: 0.86, color: Theme.magenta)
        c.text("86%", 642, 411, 40, 18, size: 12, weight: .bold, color: Theme.text, align: .right)
    } else {
        c.text("Opacity", 328, 414, 100, 18, size: 13, color: Theme.dim)
        c.pill("Fixed", 454, 406, 64, 24, fill: Theme.panelRaised, textColor: Theme.muted)
    }

    if spec.showSessionFilters {
        c.text("Sessions", 328, 454, 100, 18, size: 13, color: Theme.secondary)
        c.checkbox(454, 450, checked: true, label: "Practice")
        c.checkbox(568, 450, checked: true, label: "Race")
    } else {
        c.text("Sessions", 328, 454, 100, 18, size: 13, color: Theme.dim)
        c.pill(spec.id == "gap-to-leader" ? "Race only" : "Always", 454, 446, 88, 24, fill: NSColor(red255: 34, green: 18, blue: 52), textColor: Theme.amber)
    }

    drawPanelHeader(c, x: 726, y: 272, width: 414, height: 226, title: spec.id == "relative" ? "Relative Preview" : "Outrun Preview")
    let previewImage = renderOverlayScreenshot(spec)
    c.fill(750, 324, 366, 132, NSColor(red255: 3, green: 8, blue: 18), radius: 10)
    c.stroke(750, 324, 366, 132, Theme.cyan.withAlphaComponent(0.65), lineWidth: 1, radius: 10)
    c.imageAspectFit(previewImage, inTopRect: CGRect(x: 762, y: 334, width: 342, height: 112))
    c.text("Default size", 750, 468, 100, 18, size: 12, color: Theme.muted)
    c.text("\(Int(spec.defaultSize.width)) x \(Int(spec.defaultSize.height))", 852, 468, 120, 18, size: 12, weight: .bold, color: Theme.secondary, mono: true)

    c.fill(306, 518, 834, 70, NSColor(red255: 9, green: 18, blue: 34, alpha: 0.96), radius: 12)
    c.stroke(306, 518, 834, 70, Theme.borderDim, lineWidth: 1, radius: 12)
    c.text(spec.route == nil ? "Runtime Surface" : "Browser Source", 328, 532, 130, 18, size: 14, weight: .heavy)
    if let route = spec.route {
        c.fill(462, 542, 470, 30, NSColor(red255: 4, green: 9, blue: 20), radius: 8)
        c.stroke(462, 542, 470, 30, Theme.borderDim, lineWidth: 1, radius: 8)
        c.text("http://localhost:5011\(route)", 478, 550, 430, 18, size: 12, color: NSColor(red255: 159, green: 220, blue: 255), mono: true)
        c.fill(950, 542, 70, 30, NSColor(red255: 36, green: 17, blue: 56), radius: 8)
        c.centered("Copy", 950, 541, 70, 30, size: 12, weight: .heavy)
    } else {
        c.text("Native compact overlay. No localhost page is registered for this surface.", 328, 558, 460, 18, size: 12, color: Theme.muted)
        c.pill("NATIVE", 806, 542, 82, 28, fill: NSColor(red255: 8, green: 40, blue: 54), textColor: Theme.cyan)
    }

    c.text(spec.options.joined(separator: "  /  "), 328, 594, 560, 18, size: 11, color: Theme.dim)
}

private func drawContentHeader(_ c: Canvas, title: String, subtitle: String, status: String) {
    c.fill(278, 116, 890, 70, NSColor(red255: 16, green: 22, blue: 50, alpha: 0.9), radius: 16)
    c.fill(278, 184, 890, 2, Theme.magenta)
    c.fill(278, 186, 890, 1, Theme.cyan)
    c.text(title, 306, 134, 520, 32, size: 26, weight: .black)
    c.text(subtitle, 306, 164, 570, 18, size: 12, color: Theme.muted)
    c.pill(status, 1012, 134, 112, 30, fill: NSColor(red255: 10, green: 47, blue: 63), textColor: Theme.cyan)
}

private func drawPanelHeader(_ c: Canvas, x: CGFloat, y: CGFloat, width: CGFloat, height: CGFloat, title: String) {
    c.fill(x, y, width, height, NSColor(red255: 9, green: 18, blue: 34, alpha: 0.96), radius: 12)
    c.stroke(x, y, width, height, Theme.borderDim, lineWidth: 1, radius: 12)
    c.text(title, x + 22, y + 18, width - 44, 20, size: 15, weight: .heavy)
    c.line(x + 22, y + 48, x + width - 22, y + 48, Theme.borderDim)
}

private func statusRow(_ c: Canvas, x: CGFloat, y: CGFloat, label: String, value: String, color: NSColor) {
    c.text(label, x, y, 120, 18, size: 12, color: Theme.muted)
    c.circle(x + 138, y + 9, radius: 4, color: color, fill: true)
    c.text(value, x + 154, y, 210, 18, size: 13, weight: .bold, color: Theme.secondary)
}

private func drawOutrunBackdrop(_ c: Canvas) {
    c.gradient(0, 0, c.size.width, c.size.height, colors: [Theme.bgTop, Theme.bgMid, Theme.bgBottom], angle: -55)

    c.clipRect(c.size.width - 288, 48, 176, 176, radius: 88) {
        c.gradient(c.size.width - 288, 48, 176, 176, colors: [Theme.amber, Theme.orange, Theme.magenta, Theme.purple], angle: 90)
        for offset in stride(from: CGFloat(44), through: CGFloat(142), by: 22) {
            c.fill(c.size.width - 300, 48 + offset, 200, offset > 100 ? 12 : 8, Theme.bgTop.withAlphaComponent(0.92))
        }
    }

    let gridTop = c.size.height * 0.58
    c.gradient(0, gridTop, c.size.width, c.size.height - gridTop, colors: [Theme.cyan.withAlphaComponent(0.02), Theme.cyan.withAlphaComponent(0.12), Theme.magenta.withAlphaComponent(0.40)], angle: 90)
    for y in stride(from: gridTop + 16, through: c.size.height - 14, by: 24) {
        let alpha = min(0.5, 0.14 + (y - gridTop) / 460)
        c.line(0, y, c.size.width, y, Theme.cyan.withAlphaComponent(alpha), width: 1)
    }
    let centerX = c.size.width / 2
    for x in stride(from: CGFloat(-180), through: c.size.width + 180, by: 150) {
        c.line(centerX, gridTop, x, c.size.height, Theme.magenta.withAlphaComponent(0.42), width: 1)
    }
}

private func renderOverlayScreenshot(_ spec: OverlaySpec) -> NSImage {
    renderImage(size: spec.defaultSize) { c in
        switch spec.kind {
        case .standings:
            drawStandingsOverlay(c, title: spec.displayName)
        case .relative:
            drawRelativeOverlay(c)
        case .gap:
            drawGapOverlay(c)
        case .trackMap:
            drawTrackMapOverlay(c)
        case .streamChat:
            drawStreamChatOverlay(c)
        case .garageCover:
            drawGarageCoverOverlay(c)
        case .fuel:
            drawFuelOverlay(c)
        case .inputs:
            drawInputsOverlay(c)
        case .radar:
            drawRadarOverlay(c)
        case .flags:
            drawFlagsOverlay(c)
        case .sessionWeather:
            drawSessionWeatherOverlay(c)
        case .pitService:
            drawPitServiceOverlay(c)
        }
    }
}

private func drawOverlayShell(_ c: Canvas, title: String, status: String, footer: String? = nil) {
    c.gradient(0, 0, c.size.width, c.size.height, colors: [NSColor(red255: 11, green: 8, blue: 25, alpha: 0.96), NSColor(red255: 5, green: 16, blue: 31, alpha: 0.96)], angle: -25, radius: 10)
    c.stroke(0, 0, c.size.width, c.size.height, Theme.cyan.withAlphaComponent(0.72), lineWidth: 1, radius: 10)
    c.fill(0, 0, c.size.width, 38, Theme.titleBar, radius: 10)
    c.fill(0, 37, c.size.width, 2, Theme.magenta)
    c.text(title, 14, 10, c.size.width * 0.58, 18, size: 14, weight: .heavy)
    c.pill(status, c.size.width - 92, 8, 76, 22, fill: NSColor(red255: 10, green: 48, blue: 62), textColor: Theme.cyan)
    if let footer {
        c.text(footer, 14, c.size.height - 24, c.size.width - 28, 16, size: 10, color: Theme.dim)
    }
}

private func drawStandingsOverlay(_ c: Canvas, title: String) {
    drawOverlayShell(c, title: title, status: "RACE", footer: "live scoring - class timing - quiet source footer")
    let headers = ["P", "Driver", "Class", "Gap", "Last", "Stint"]
    let cols: [CGFloat] = [20, 58, 300, 384, 454, 536]
    for (index, header) in headers.enumerated() {
        c.text(header, cols[index], 56, index == 1 ? 220 : 58, 16, size: 10, weight: .heavy, color: index == 1 ? Theme.magenta : Theme.cyan)
    }
    let rows = [
        ("1", "D. Alvarez", "GT3", "LEAD", "8:13.428", "12L", false, Theme.green),
        ("2", "TMR Team", "GT3", "+4.2", "8:12.904", "11L", true, Theme.cyan),
        ("3", "M. Reiter", "GT3", "+18.6", "8:14.221", "10L", false, Theme.magenta),
        ("4", "S. Kline", "GTP", "-1L", "7:44.801", "FAST", false, Theme.amber),
        ("5", "A. Moreau", "GT3", "+44.0", "8:16.002", "PIT", false, Theme.orange)
    ]
    for (idx, row) in rows.enumerated() {
        let y = CGFloat(80 + idx * 42)
        c.fill(14, y, c.size.width - 28, 34, row.6 ? NSColor(red255: 7, green: 45, blue: 54, alpha: 0.82) : NSColor(red255: 12, green: 21, blue: 39, alpha: 0.72), radius: 6)
        c.fill(18, y + 6, 4, 22, row.7, radius: 2)
        c.text(row.0, 28, y + 9, 24, 16, size: 12, weight: .bold, mono: true)
        c.text(row.1, 58, y + 9, 220, 16, size: 12, weight: row.6 ? .heavy : .semibold)
        c.text(row.2, 300, y + 9, 52, 16, size: 11, weight: .bold, color: row.7)
        c.text(row.3, 384, y + 9, 50, 16, size: 12, weight: .bold, color: row.3 == "LEAD" ? Theme.green : Theme.secondary, align: .right, mono: true)
        c.text(row.4, 454, y + 9, 62, 16, size: 12, color: Theme.secondary, align: .right, mono: true)
        c.text(row.5, 536, y + 9, 54, 16, size: 11, weight: .bold, color: Theme.amber, align: .right)
    }
}

private func drawRelativeOverlay(_ c: Canvas) {
    drawOverlayShell(c, title: "Relative", status: "IN CAR", footer: "proximity first - timing fallback hidden when healthy")
    let rows = [
        ("-2", "M. Reiter", "-2.4", "GT3", Theme.magenta, false),
        ("-1", "D. Alvarez", "-0.8", "GT3", Theme.green, false),
        ("REF", "TMR Team", "0.0", "GT3", Theme.cyan, true),
        ("+1", "A. Moreau", "+0.7", "GT3", Theme.magenta, false),
        ("+2", "S. Kline", "+3.8", "GTP", Theme.amber, false),
        ("+3", "R. Cho", "+7.1", "GT3", Theme.magenta, false)
    ]
    for (idx, row) in rows.enumerated() {
        let y = CGFloat(56 + idx * 39)
        c.fill(14, y, c.size.width - 28, 31, row.5 ? NSColor(red255: 7, green: 45, blue: 54, alpha: 0.86) : NSColor(red255: 12, green: 21, blue: 39, alpha: 0.72), radius: 6)
        c.fill(20, y + 8, 34, 4, row.4, radius: 2)
        c.text(row.0, 24, y + 10, 42, 16, size: 11, weight: .bold, color: row.5 ? Theme.cyan : Theme.muted, mono: true)
        c.text(row.1, 84, y + 8, 220, 18, size: 13, weight: row.5 ? .heavy : .semibold)
        c.text(row.2, c.size.width - 112, y + 8, 54, 18, size: 13, weight: .bold, align: .right, mono: true)
        c.text(row.3, c.size.width - 50, y + 8, 32, 18, size: 11, weight: .bold, color: row.4, align: .right)
    }
}

private func drawGapOverlay(_ c: Canvas) {
    drawOverlayShell(c, title: "Gap To Leader", status: "4H", footer: "class leader baseline - weather bands - driver swap ticks")
    let plot = CGRect(x: 44, y: 58, width: c.size.width - 188, height: 154)
    c.fill(plot.minX, plot.minY, plot.width, plot.height, NSColor(red255: 5, green: 11, blue: 23), radius: 8)
    c.stroke(plot.minX, plot.minY, plot.width, plot.height, Theme.borderDim, lineWidth: 1, radius: 8)
    for i in 0...4 {
        let y = plot.minY + CGFloat(i) * plot.height / 4
        c.line(plot.minX, y, plot.maxX, y, Theme.borderDim.withAlphaComponent(0.55))
    }
    c.fill(plot.minX + 176, plot.minY, 42, plot.height, Theme.cyan.withAlphaComponent(0.08))
    c.fill(plot.minX + 300, plot.minY, 54, plot.height, Theme.magenta.withAlphaComponent(0.10))
    c.polyline([
        CGPoint(x: plot.minX + 4, y: plot.minY + 26),
        CGPoint(x: plot.minX + 90, y: plot.minY + 24),
        CGPoint(x: plot.minX + 190, y: plot.minY + 26),
        CGPoint(x: plot.minX + 306, y: plot.minY + 24),
        CGPoint(x: plot.maxX - 6, y: plot.minY + 25)
    ], color: Theme.green, width: 2)
    c.polyline([
        CGPoint(x: plot.minX + 4, y: plot.minY + 86),
        CGPoint(x: plot.minX + 90, y: plot.minY + 74),
        CGPoint(x: plot.minX + 190, y: plot.minY + 96),
        CGPoint(x: plot.minX + 306, y: plot.minY + 104),
        CGPoint(x: plot.maxX - 6, y: plot.minY + 92)
    ], color: Theme.cyan, width: 2.4)
    c.polyline([
        CGPoint(x: plot.minX + 4, y: plot.minY + 126),
        CGPoint(x: plot.minX + 90, y: plot.minY + 118),
        CGPoint(x: plot.minX + 190, y: plot.minY + 132),
        CGPoint(x: plot.minX + 306, y: plot.minY + 138),
        CGPoint(x: plot.maxX - 6, y: plot.minY + 118)
    ], color: Theme.magenta, width: 1.8)
    let x = c.size.width - 124
    metricBlock(c, x: x, y: 70, label: "FOCUS", value: "P2", color: Theme.cyan)
    metricBlock(c, x: x, y: 124, label: "LEADER", value: "+4.2", color: Theme.green)
    metricBlock(c, x: x, y: 178, label: "WINDOW", value: "1h42", color: Theme.amber)
}

private func drawTrackMapOverlay(_ c: Canvas) {
    c.gradient(0, 0, c.size.width, c.size.height, colors: [NSColor(red255: 4, green: 9, blue: 19), NSColor(red255: 13, green: 7, blue: 29)], angle: -20, radius: 12)
    c.stroke(0, 0, c.size.width, c.size.height, Theme.cyan.withAlphaComponent(0.6), lineWidth: 1, radius: 12)
    c.text("Track Map", 16, 14, 160, 18, size: 14, weight: .heavy)
    c.pill("P12", c.size.width - 74, 10, 58, 22, fill: NSColor(red255: 10, green: 48, blue: 62), textColor: Theme.cyan)
    let points = [
        CGPoint(x: 80, y: 208), CGPoint(x: 68, y: 134), CGPoint(x: 122, y: 80),
        CGPoint(x: 218, y: 66), CGPoint(x: 288, y: 114), CGPoint(x: 296, y: 206),
        CGPoint(x: 238, y: 282), CGPoint(x: 134, y: 290), CGPoint(x: 80, y: 208)
    ]
    c.polyline(points, color: NSColor.white.withAlphaComponent(0.88), width: 10)
    c.polyline(points, color: Theme.cyan.withAlphaComponent(0.55), width: 2)
    c.polyline(Array(points[1...3]), color: Theme.green, width: 5)
    c.circle(218, 66, radius: 8, color: Theme.magenta, fill: true)
    c.text("PB S2", 232, 58, 54, 16, size: 10, weight: .bold, color: Theme.green)
    c.circle(288, 114, radius: 6, color: Theme.amber, fill: true)
    c.circle(134, 290, radius: 6, color: Theme.cyan, fill: true)
    c.circle(238, 282, radius: 5, color: Theme.magenta, fill: true)
    c.text("Gesamtstrecke VLN", 18, c.size.height - 32, c.size.width - 36, 16, size: 10, color: Theme.dim)
}

private func drawStreamChatOverlay(_ c: Canvas) {
    drawOverlayShell(c, title: "Stream Chat", status: "TWITCH")
    let messages: [(String, String, NSColor)] = [
        ("pitwall", "Box this lap if traffic stays clear.", Theme.cyan),
        ("tmr_fan", "Purple sector!", Theme.magenta),
        ("crew", "Fuel target still 8 laps.", Theme.amber),
        ("mod", "Setup cover armed for garage.", Theme.green)
    ]
    for (idx, item) in messages.enumerated() {
        let y = CGFloat(58 + idx * 84)
        c.fill(18, y, c.size.width - 36, 62, NSColor(red255: 12, green: 21, blue: 39, alpha: 0.76), radius: 10)
        c.text(item.0, 34, y + 14, 140, 16, size: 12, weight: .bold, color: item.2)
        c.text(item.1, 34, y + 36, c.size.width - 68, 16, size: 12, color: Theme.secondary)
    }
    c.fill(18, c.size.height - 60, c.size.width - 36, 36, NSColor(red255: 4, green: 9, blue: 20), radius: 18)
    c.text("Connected as TechMatesRacing", 38, c.size.height - 50, c.size.width - 76, 16, size: 11, color: Theme.dim)
}

private func drawGarageCoverOverlay(_ c: Canvas) {
    drawOutrunBackdrop(c)
    c.fill(0, 0, c.size.width, c.size.height, NSColor.black.withAlphaComponent(0.32))
    c.stroke(24, 24, c.size.width - 48, c.size.height - 48, Theme.cyan.withAlphaComponent(0.72), lineWidth: 2, radius: 18)
    c.fill(74, 86, 118, 62, NSColor(red255: 17, green: 26, blue: 53), radius: 10)
    c.stroke(74, 86, 118, 62, Theme.magenta, lineWidth: 2, radius: 10)
    c.centered("TMR", 74, 82, 118, 62, size: 34, weight: .black)
    c.text("GARAGE COVER", 226, 80, 600, 58, size: 54, weight: .black)
    c.text("Setup screen privacy surface", 232, 142, 460, 26, size: 18, weight: .bold, color: Theme.cyan)
    c.fill(0, 494, c.size.width, 72, Theme.magenta.withAlphaComponent(0.72))
    c.fill(0, 566, c.size.width, 12, Theme.amber.withAlphaComponent(0.92))
    c.text("LIVE GARAGE DETECTED", 84, 532, 400, 24, size: 22, weight: .heavy, color: Theme.text)
    c.text("TMR Overlay fails closed until iRacing returns to the car.", 84, 604, 620, 24, size: 18, weight: .semibold, color: Theme.secondary)
}

private func drawFuelOverlay(_ c: Canvas) {
    drawOverlayShell(c, title: "Fuel Calculator", status: "RACE", footer: "measured burn plus exact car/track/session history")
    let cols: [CGFloat] = [20, 148, 270, 388, 504]
    let headers = ["Metric", "Now", "Target", "Pit", "Note"]
    for (idx, header) in headers.enumerated() {
        c.text(header, cols[idx], 56, 90, 16, size: 10, weight: .heavy, color: idx == 0 ? Theme.magenta : Theme.cyan)
    }
    let rows = [
        ("Fuel", "48.2 L", "62.5 L", "+39 L", "8 lap stint"),
        ("Burn", "7.21", "7.00", "safe", "measured"),
        ("Laps", "6.6", "8", "5 stops", "target"),
        ("Source", "live", "history", "pit", "green lap")
    ]
    for (idx, row) in rows.enumerated() {
        let y = CGFloat(82 + idx * 43)
        c.fill(14, y, c.size.width - 28, 34, NSColor(red255: 12, green: 21, blue: 39, alpha: 0.72), radius: 6)
        c.text(row.0, cols[0], y + 9, 100, 16, size: 12, weight: .bold)
        c.text(row.1, cols[1], y + 9, 86, 16, size: 12, color: Theme.secondary, mono: true)
        c.text(row.2, cols[2], y + 9, 86, 16, size: 12, color: Theme.cyan, mono: true)
        c.text(row.3, cols[3], y + 9, 82, 16, size: 12, color: Theme.amber, mono: true)
        c.text(row.4, cols[4], y + 9, 78, 16, size: 12, weight: .bold, color: idx == 2 ? Theme.green : Theme.secondary)
    }
}

private func drawInputsOverlay(_ c: Canvas) {
    drawOverlayShell(c, title: "Inputs", status: "LIVE")
    c.text("GEAR", 30, 64, 70, 16, size: 11, weight: .heavy, color: Theme.muted)
    c.text("4", 30, 84, 76, 64, size: 58, weight: .black, color: Theme.cyan, mono: true)
    c.text("142 mph", 116, 98, 110, 24, size: 22, weight: .heavy, color: Theme.text, mono: true)
    inputBar(c, x: 250, y: 70, label: "THR", value: 0.82, color: Theme.green)
    inputBar(c, x: 250, y: 114, label: "BRK", value: 0.18, color: Theme.red)
    inputBar(c, x: 250, y: 158, label: "STR", value: 0.58, color: Theme.cyan)
    c.pill("ABS 2", 34, 164, 72, 24, fill: NSColor(red255: 38, green: 20, blue: 52), textColor: Theme.amber)
    c.pill("TC 4", 116, 164, 72, 24, fill: NSColor(red255: 8, green: 42, blue: 54), textColor: Theme.cyan)
}

private func drawRadarOverlay(_ c: Canvas) {
    c.gradient(0, 0, c.size.width, c.size.height, colors: [NSColor(red255: 3, green: 8, blue: 18), NSColor(red255: 13, green: 7, blue: 29)], angle: -25, radius: c.size.width / 2)
    c.circle(c.size.width / 2, c.size.height / 2, radius: 138, color: Theme.cyan.withAlphaComponent(0.65), width: 2)
    c.circle(c.size.width / 2, c.size.height / 2, radius: 92, color: Theme.cyan.withAlphaComponent(0.28), width: 1)
    c.circle(c.size.width / 2, c.size.height / 2, radius: 46, color: Theme.cyan.withAlphaComponent(0.20), width: 1)
    c.line(c.size.width / 2, 18, c.size.width / 2, c.size.height - 18, Theme.borderDim)
    c.line(18, c.size.height / 2, c.size.width - 18, c.size.height / 2, Theme.borderDim)
    c.fill(c.size.width / 2 - 10, c.size.height / 2 - 22, 20, 44, Theme.text, radius: 4)
    c.fill(58, 122, 28, 58, Theme.red, radius: 6)
    c.fill(216, 86, 26, 52, Theme.amber, radius: 6)
    c.fill(198, 206, 24, 44, Theme.cyan.withAlphaComponent(0.9), radius: 6)
    c.text("CAR RADAR", 24, 24, 150, 18, size: 13, weight: .heavy)
    c.text("3.4s GTP", 194, 44, 82, 16, size: 11, weight: .bold, color: Theme.red, align: .right, mono: true)
}

private func drawFlagsOverlay(_ c: Canvas) {
    c.gradient(0, 0, c.size.width, c.size.height, colors: [NSColor(red255: 4, green: 9, blue: 19, alpha: 0.95), NSColor(red255: 17, green: 8, blue: 28, alpha: 0.95)], angle: -25, radius: 14)
    c.stroke(0, 0, c.size.width, c.size.height, Theme.cyan.withAlphaComponent(0.65), lineWidth: 1, radius: 14)
    c.text("FLAGS", 18, 16, 120, 18, size: 14, weight: .heavy)
    flagCard(c, x: 24, y: 54, label: "Y", color: Theme.amber, caption: "LOCAL")
    flagCard(c, x: 126, y: 54, label: "B", color: NSColor(red255: 60, green: 140, blue: 255), caption: "BLUE")
    flagCard(c, x: 228, y: 54, label: "W", color: Theme.text, caption: "WHITE")
}

private func drawSessionWeatherOverlay(_ c: Canvas) {
    drawOverlayShell(c, title: "Session / Weather", status: "RACE")
    metricGrid(c, [
        ("Time", "2:14:08", Theme.cyan),
        ("Lap", "18 / 29", Theme.text),
        ("Track", "23 C", Theme.green),
        ("Air", "19 C", Theme.secondary),
        ("Wetness", "Damp", Theme.amber),
        ("Wind", "9 mph NE", Theme.secondary)
    ])
}

private func drawPitServiceOverlay(_ c: Canvas) {
    drawOverlayShell(c, title: "Pit Service", status: "ARMED")
    c.fill(24, 66, c.size.width - 48, 54, NSColor(red255: 6, green: 45, blue: 39, alpha: 0.82), radius: 10)
    c.text("PIT REQUEST", 44, 82, 140, 18, size: 12, weight: .heavy, color: Theme.green)
    c.text("ON - box this lap", 198, 80, 170, 20, size: 17, weight: .heavy)
    serviceRow(c, y: 140, label: "Fuel", value: "+39 L", color: Theme.cyan)
    serviceRow(c, y: 174, label: "Tires", value: "Left side", color: Theme.amber)
    serviceRow(c, y: 208, label: "Service", value: "Complete on green", color: Theme.green)
}

private func inputBar(_ c: Canvas, x: CGFloat, y: CGFloat, label: String, value: CGFloat, color: NSColor) {
    c.text(label, x, y + 2, 38, 16, size: 11, weight: .heavy, color: Theme.muted)
    c.fill(x + 46, y + 4, 204, 12, Theme.panelRaised, radius: 6)
    c.fill(x + 46, y + 4, 204 * value, 12, color, radius: 6)
    c.text("\(Int(value * 100))%", x + 260, y, 48, 18, size: 12, weight: .bold, color: Theme.secondary, align: .right, mono: true)
}

private func metricBlock(_ c: Canvas, x: CGFloat, y: CGFloat, label: String, value: String, color: NSColor) {
    c.fill(x, y, 90, 40, NSColor(red255: 12, green: 21, blue: 39, alpha: 0.78), radius: 8)
    c.text(label, x + 10, y + 8, 70, 12, size: 9, weight: .heavy, color: Theme.muted)
    c.text(value, x + 10, y + 20, 70, 16, size: 14, weight: .heavy, color: color, mono: true)
}

private func metricGrid(_ c: Canvas, _ values: [(String, String, NSColor)]) {
    for (idx, item) in values.enumerated() {
        let col = idx % 2
        let row = idx / 2
        let x = CGFloat(24 + col * 190)
        let y = CGFloat(62 + row * 55)
        c.fill(x, y, 166, 42, NSColor(red255: 12, green: 21, blue: 39, alpha: 0.78), radius: 8)
        c.text(item.0, x + 12, y + 9, 72, 14, size: 10, weight: .heavy, color: Theme.muted)
        c.text(item.1, x + 82, y + 8, 70, 18, size: 14, weight: .heavy, color: item.2, align: .right, mono: true)
    }
}

private func flagCard(_ c: Canvas, x: CGFloat, y: CGFloat, label: String, color: NSColor, caption: String) {
    c.fill(x, y, 82, 82, color, radius: 10)
    c.centered(label, x, y + 4, 82, 52, size: 42, weight: .black, color: color == Theme.text ? Theme.bgBottom : Theme.bgBottom)
    c.centered(caption, x, y + 58, 82, 18, size: 10, weight: .heavy, color: Theme.bgBottom)
}

private func serviceRow(_ c: Canvas, y: CGFloat, label: String, value: String, color: NSColor) {
    c.fill(24, y, c.size.width - 48, 24, NSColor(red255: 12, green: 21, blue: 39, alpha: 0.76), radius: 6)
    c.text(label, 42, y + 5, 100, 14, size: 11, weight: .heavy, color: Theme.muted)
    c.text(value, 150, y + 4, c.size.width - 198, 16, size: 12, weight: .bold, color: color, align: .right)
}

private func renderContactSheet(
    title: String,
    subtitle: String,
    items: [(id: String, title: String, image: NSImage)],
    columns: Int,
    thumbSize: CGSize
) -> NSImage {
    let rows = Int(ceil(Double(items.count) / Double(columns)))
    let margin: CGFloat = 44
    let gutter: CGFloat = 32
    let headerHeight: CGFloat = 120
    let cardWidth = thumbSize.width + 32
    let cardHeight = thumbSize.height + 72
    let size = CGSize(
        width: margin * 2 + CGFloat(columns) * cardWidth + CGFloat(columns - 1) * gutter,
        height: margin * 2 + headerHeight + CGFloat(rows) * cardHeight + CGFloat(rows - 1) * gutter
    )

    return renderImage(size: size) { c in
        drawOutrunBackdrop(c)
        c.fill(0, 0, size.width, size.height, NSColor.black.withAlphaComponent(0.18))
        c.text(title, margin, 36, size.width - margin * 2, 42, size: 34, weight: .black)
        c.text(subtitle, margin, 78, size.width - margin * 2, 22, size: 15, color: Theme.secondary)
        for (idx, item) in items.enumerated() {
            let col = idx % columns
            let row = idx / columns
            let x = margin + CGFloat(col) * (cardWidth + gutter)
            let y = margin + headerHeight + CGFloat(row) * (cardHeight + gutter)
            c.fill(x, y, cardWidth, cardHeight, NSColor(red255: 7, green: 14, blue: 27, alpha: 0.94), radius: 12)
            c.stroke(x, y, cardWidth, cardHeight, Theme.border, lineWidth: 1, radius: 12)
            c.text("\(idx + 1). \(item.title)", x + 16, y + 16, cardWidth - 32, 22, size: 16, weight: .heavy)
            c.text(item.id, x + 16, y + 42, cardWidth - 32, 16, size: 11, color: Theme.dim, mono: true)
            c.fill(x + 16, y + 62, thumbSize.width, thumbSize.height, NSColor(red255: 3, green: 8, blue: 18), radius: 8)
            c.stroke(x + 16, y + 62, thumbSize.width, thumbSize.height, Theme.cyan.withAlphaComponent(0.45), lineWidth: 1, radius: 8)
            c.imageAspectFit(item.image, inTopRect: CGRect(x: x + 24, y: y + 70, width: thumbSize.width - 16, height: thumbSize.height - 16))
        }
    }
}

private func renderImage(size: CGSize, draw: (Canvas) -> Void) -> NSImage {
    let image = NSImage(size: size)
    image.lockFocus()
    NSGraphicsContext.current?.imageInterpolation = .high
    NSColor.clear.setFill()
    NSBezierPath(rect: CGRect(origin: .zero, size: size)).fill()
    draw(Canvas(size: size))
    image.unlockFocus()
    return image
}

private func writePNG(_ image: NSImage, to url: URL) throws {
    guard
        let tiff = image.tiffRepresentation,
        let bitmap = NSBitmapImageRep(data: tiff),
        let data = bitmap.representation(using: .png, properties: [:])
    else {
        throw NSError(domain: "OutrunRenderer", code: 1, userInfo: [NSLocalizedDescriptionKey: "Could not encode PNG for \(url.path)."])
    }
    try data.write(to: url)
}
