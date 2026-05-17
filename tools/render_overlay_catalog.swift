import AppKit
import Foundation

private enum Theme {
    static let border = NSColor(white: 1.0, alpha: 0.28)
    static let background = NSColor(red255: 14, green: 18, blue: 21, alpha: 0.94)
    static let panel = NSColor(red255: 24, green: 30, blue: 34, alpha: 0.96)
    static let panelAlt = NSColor(red255: 31, green: 38, blue: 43, alpha: 0.96)
    static let text = NSColor.white
    static let secondary = NSColor(red255: 218, green: 226, blue: 230)
    static let muted = NSColor(red255: 128, green: 145, blue: 153)
    static let subtle = NSColor(white: 0.70, alpha: 1.0)
    static let success = NSColor(red255: 112, green: 224, blue: 146)
    static let successBackground = NSColor(red255: 18, green: 46, blue: 34, alpha: 0.96)
    static let info = NSColor(red255: 140, green: 190, blue: 245)
    static let infoBackground = NSColor(red255: 18, green: 30, blue: 42, alpha: 0.96)
    static let warning = NSColor(red255: 246, green: 184, blue: 88)
    static let warningBackground = NSColor(red255: 64, green: 46, blue: 14, alpha: 0.96)
    static let error = NSColor(red255: 236, green: 112, blue: 99)
    static let errorBackground = NSColor(red255: 70, green: 18, blue: 24, alpha: 0.96)
    static let pink = NSColor(red255: 255, green: 86, blue: 151)
    static let cyan = NSColor(red255: 69, green: 203, blue: 250)
    static let purple = NSColor(red255: 192, green: 132, blue: 252)
    static let track = NSColor(red255: 95, green: 105, blue: 112)

    static func font(_ size: CGFloat, weight: NSFont.Weight = .regular) -> NSFont {
        NSFont(name: "SF Pro", size: size) ?? NSFont.systemFont(ofSize: size, weight: weight)
    }
}

private extension NSColor {
    convenience init(red255 red: CGFloat, green: CGFloat, blue: CGFloat, alpha: CGFloat = 1.0) {
        self.init(calibratedRed: red / 255.0, green: green / 255.0, blue: blue / 255.0, alpha: alpha)
    }
}

private struct DriverRow {
    let position: String
    let name: String
    let cls: String
    let gap: String
    let last: String
    let badge: String
    let color: NSColor
    let highlight: Bool
}

private final class Canvas {
    let size: CGSize
    let context: CGContext

    init(size: CGSize, context: CGContext) {
        self.size = size
        self.context = context
    }

    func fill(_ rect: CGRect, _ color: NSColor, radius: CGFloat = 0) {
        color.setFill()
        path(rect, radius).fill()
    }

    func stroke(_ rect: CGRect, _ color: NSColor, width: CGFloat = 1, radius: CGFloat = 0) {
        color.setStroke()
        let p = path(rect.insetBy(dx: width / 2, dy: width / 2), radius)
        p.lineWidth = width
        p.stroke()
    }

    func line(from: CGPoint, to: CGPoint, color: NSColor, width: CGFloat = 1) {
        color.setStroke()
        let p = NSBezierPath()
        p.move(to: from)
        p.line(to: to)
        p.lineWidth = width
        p.stroke()
    }

    func text(
        _ value: String,
        _ rect: CGRect,
        size: CGFloat,
        weight: NSFont.Weight = .regular,
        color: NSColor = Theme.text,
        align: NSTextAlignment = .left
    ) {
        let paragraph = NSMutableParagraphStyle()
        paragraph.alignment = align
        paragraph.lineBreakMode = .byTruncatingTail
        let attrs: [NSAttributedString.Key: Any] = [
            .font: Theme.font(size, weight: weight),
            .foregroundColor: color,
            .paragraphStyle: paragraph
        ]
        NSString(string: value).draw(in: rect, withAttributes: attrs)
    }

    func centered(_ value: String, _ rect: CGRect, size: CGFloat, weight: NSFont.Weight = .semibold, color: NSColor = Theme.text) {
        let y = rect.minY + max(0, (rect.height - size * 1.35) / 2)
        text(value, CGRect(x: rect.minX, y: y, width: rect.width, height: size * 1.45), size: size, weight: weight, color: color, align: .center)
    }

    func pill(_ value: String, _ rect: CGRect, color: NSColor, textColor: NSColor = Theme.text) {
        fill(rect, color, radius: 5)
        centered(value, rect.insetBy(dx: 6, dy: 0), size: 11, weight: .bold, color: textColor)
    }

    func title(_ title: String, subtitle: String? = nil, status: String? = nil) {
        fill(CGRect(x: 0, y: size.height - 34, width: size.width, height: 34), Theme.panel, radius: 0)
        text(title, CGRect(x: 12, y: size.height - 27, width: size.width * 0.55, height: 20), size: 13, weight: .bold)
        if let subtitle {
            text(subtitle, CGRect(x: 12, y: size.height - 48, width: size.width - 24, height: 18), size: 10, color: Theme.muted)
        }
        if let status {
            pill(status, CGRect(x: size.width - 94, y: size.height - 27, width: 80, height: 20), color: Theme.infoBackground, textColor: Theme.info)
        }
        line(from: CGPoint(x: 0, y: size.height - 34), to: CGPoint(x: size.width, y: size.height - 34), color: Theme.border, width: 1)
    }

    func roundedPanel(_ rect: CGRect, title: String? = nil) {
        fill(rect, Theme.background, radius: 7)
        stroke(rect, Theme.border, width: 1, radius: 7)
        if let title {
            fill(CGRect(x: rect.minX, y: rect.maxY - 32, width: rect.width, height: 32), Theme.panel, radius: 7)
            text(title, CGRect(x: rect.minX + 12, y: rect.maxY - 24, width: rect.width - 24, height: 18), size: 12, weight: .bold)
        }
    }

    func bar(_ rect: CGRect, value: CGFloat, color: NSColor, background: NSColor = Theme.panelAlt) {
        fill(rect, background, radius: 3)
        fill(CGRect(x: rect.minX, y: rect.minY, width: max(4, rect.width * min(max(value, 0), 1)), height: rect.height), color, radius: 3)
    }

    func polyline(_ points: [CGPoint], color: NSColor, width: CGFloat = 2) {
        guard let first = points.first else { return }
        color.setStroke()
        let p = NSBezierPath()
        p.move(to: first)
        for point in points.dropFirst() {
            p.line(to: point)
        }
        p.lineWidth = width
        p.stroke()
    }

    func circle(center: CGPoint, radius: CGFloat, color: NSColor, fill: Bool = false, width: CGFloat = 1) {
        let rect = CGRect(x: center.x - radius, y: center.y - radius, width: radius * 2, height: radius * 2)
        if fill {
            color.setFill()
            NSBezierPath(ovalIn: rect).fill()
        } else {
            color.setStroke()
            let p = NSBezierPath(ovalIn: rect)
            p.lineWidth = width
            p.stroke()
        }
    }

    private func path(_ rect: CGRect, _ radius: CGFloat) -> NSBezierPath {
        radius <= 0 ? NSBezierPath(rect: rect) : NSBezierPath(roundedRect: rect, xRadius: radius, yRadius: radius)
    }
}

private let drivers: [DriverRow] = [
    DriverRow(position: "1", name: "D. Alvarez", cls: "GT3", gap: "LEAD", last: "8:13.428", badge: "PIT 12", color: Theme.pink, highlight: false),
    DriverRow(position: "2", name: "TMR Team", cls: "GT3", gap: "+4.2", last: "8:12.904", badge: "YOU", color: Theme.success, highlight: true),
    DriverRow(position: "3", name: "M. Reiter", cls: "GT3", gap: "+18.6", last: "8:14.221", badge: "6.8k", color: Theme.pink, highlight: false),
    DriverRow(position: "4", name: "S. Kline", cls: "GTP", gap: "-1L", last: "7:44.801", badge: "FAST", color: Theme.cyan, highlight: false),
    DriverRow(position: "5", name: "A. Moreau", cls: "GT3", gap: "+44.0", last: "8:16.002", badge: "OUT", color: Theme.warning, highlight: false)
]

private let overlaySpecs: [(slug: String, title: String, size: CGSize, render: (Canvas) -> Void)] = [
    ("standings", "Standings", CGSize(width: 720, height: 420), renderStandings),
    ("horizontal-standings", "Horizontal Standings", CGSize(width: 780, height: 170), renderHorizontalStandings),
    ("leaderboard", "Leaderboard", CGSize(width: 430, height: 460), renderLeaderboard),
    ("relative", "Relative", CGSize(width: 430, height: 330), renderRelative),
    ("fuel-calculator", "Fuel Calculator", CGSize(width: 640, height: 280), renderFuel),
    ("input-telemetry", "Input Telemetry", CGSize(width: 480, height: 280), renderInputs),
    ("track-map", "Track Map", CGSize(width: 480, height: 480), renderTrackMap),
    ("flat-map", "Flat Map", CGSize(width: 620, height: 260), renderFlatMap),
    ("mini-map", "Mini Map", CGSize(width: 300, height: 300), renderMiniMap),
    ("radar", "Radar", CGSize(width: 360, height: 360), renderRadar),
    ("radar-bars", "Radar Bars", CGSize(width: 520, height: 160), renderRadarBars),
    ("blind-spot-indicator", "Blind Spot Indicator", CGSize(width: 520, height: 160), renderBlindSpot),
    ("overtake-alert", "Multiclass Traffic", CGSize(width: 500, height: 150), renderOvertakeAlert),
    ("pit-box-helper", "Pit Box Helper", CGSize(width: 560, height: 170), renderPitBox),
    ("advanced-panel", "Race Control", CGSize(width: 700, height: 420), renderRaceControl),
    ("data-blocks", "Data Blocks", CGSize(width: 560, height: 240), renderDataBlocks),
    ("delta", "Delta", CGSize(width: 520, height: 150), renderDelta),
    ("digiflag", "Digital Flag", CGSize(width: 340, height: 170), renderDigiFlag),
    ("flags", "Flags", CGSize(width: 560, height: 170), renderFlags),
    ("boost-box", "Boost Box", CGSize(width: 360, height: 210), renderBoost),
    ("g-force-meter", "G-Force Meter", CGSize(width: 340, height: 340), renderGForce),
    ("head-to-head", "Head To Head", CGSize(width: 640, height: 230), renderHeadToHead),
    ("heart-rate", "Heart Rate", CGSize(width: 320, height: 210), renderHeartRate),
    ("laptime-graph", "Lap Time Graph", CGSize(width: 640, height: 290), renderLapGraph),
    ("laptime-log", "Lap Time Log", CGSize(width: 430, height: 380), renderLapLog),
    ("laptime-spread", "Lap Time Spread", CGSize(width: 620, height: 260), renderLapSpread),
    ("race-schedule", "Race Schedule", CGSize(width: 620, height: 340), renderRaceSchedule),
    ("session-timer", "Session Timer", CGSize(width: 430, height: 150), renderSessionTimer),
    ("weather-monitor", "Weather Monitor", CGSize(width: 560, height: 260), renderWeather),
    ("twitch-chat", "Twitch Chat", CGSize(width: 430, height: 320), renderTwitchChat),
    ("garage-cover", "Garage / Setup Cover", CGSize(width: 640, height: 360), renderGarageCover)
]

private func renderBase(_ canvas: Canvas, title: String, status: String? = nil) {
    canvas.roundedPanel(CGRect(origin: .zero, size: canvas.size))
    canvas.title(title, status: status)
}

private func renderStandings(_ c: Canvas) {
    renderBase(c, title: "Standings", status: "RACE")
    header(c, y: 350, columns: [("POS", 20, 46), ("DRIVER", 74, 260), ("CLS", 350, 54), ("GAP", 424, 82), ("LAST", 526, 92), ("ST", 634, 52)])
    tableRows(c, y: 308, rowHeight: 48, rows: drivers)
}

private func renderHorizontalStandings(_ c: Canvas) {
    renderBase(c, title: "Horizontal Standings", status: "P12")
    let startX: CGFloat = 18
    for i in 0..<6 {
        let x = startX + CGFloat(i) * 124
        let color = i == 2 ? Theme.successBackground : Theme.panel
        c.fill(CGRect(x: x, y: 58, width: 112, height: 70), color, radius: 6)
        c.text("P\(10 + i)", CGRect(x: x + 10, y: 105, width: 42, height: 18), size: 11, weight: .bold, color: i == 2 ? Theme.success : Theme.secondary)
        c.text(["Moreau", "Kline", "TMR", "Fodor", "Naylor", "Chen"][i], CGRect(x: x + 10, y: 82, width: 90, height: 18), size: 12, weight: .semibold)
        c.text(i == 2 ? "YOU" : "+\(i * 3 + 2).\(i)", CGRect(x: x + 10, y: 62, width: 90, height: 16), size: 10, color: Theme.muted)
    }
}

private func renderLeaderboard(_ c: Canvas) {
    renderBase(c, title: "Leaderboard", status: "SOF 2.8k")
    for (i, row) in drivers.enumerated() {
        let y = 350 - CGFloat(i) * 58
        c.fill(CGRect(x: 16, y: y, width: 398, height: 46), row.highlight ? Theme.successBackground : Theme.panel, radius: 5)
        c.text(row.position, CGRect(x: 30, y: y + 14, width: 30, height: 18), size: 15, weight: .bold)
        c.fill(CGRect(x: 66, y: y + 8, width: 4, height: 30), row.color, radius: 2)
        c.text(row.name, CGRect(x: 84, y: y + 16, width: 190, height: 18), size: 13, weight: .bold)
        c.text(row.gap, CGRect(x: 290, y: y + 16, width: 70, height: 18), size: 13, weight: .bold, color: row.highlight ? Theme.success : Theme.secondary, align: .right)
        c.text(row.last, CGRect(x: 300, y: y + 2, width: 84, height: 14), size: 9, color: Theme.muted, align: .right)
    }
}

private func renderRelative(_ c: Canvas) {
    renderBase(c, title: "Relative", status: "FOCUS")
    let rows = [
        ("-2", "S. Kline", "-8.4", Theme.cyan),
        ("-1", "M. Reiter", "-1.2", Theme.pink),
        ("0", "TMR Team", "YOU", Theme.success),
        ("+1", "A. Moreau", "+0.8", Theme.pink),
        ("+2", "J. Foster", "+6.1", Theme.warning)
    ]
    for (i, row) in rows.enumerated() {
        let y = 250 - CGFloat(i) * 40
        c.fill(CGRect(x: 16, y: y, width: 398, height: 34), row.0 == "0" ? Theme.successBackground : Theme.panel, radius: 4)
        c.fill(CGRect(x: 26, y: y + 8, width: 4, height: 18), row.3, radius: 2)
        c.text(row.0, CGRect(x: 42, y: y + 9, width: 34, height: 16), size: 12, weight: .bold, color: Theme.muted)
        c.text(row.1, CGRect(x: 84, y: y + 9, width: 190, height: 16), size: 12, weight: .semibold)
        c.text(row.2, CGRect(x: 300, y: y + 9, width: 78, height: 16), size: 12, weight: .bold, color: row.0 == "0" ? Theme.success : Theme.secondary, align: .right)
    }
}

private func renderFuel(_ c: Canvas) {
    renderBase(c, title: "Fuel Strategy", status: "8 LAPS")
    let metrics = [("Fuel", "61.4 L"), ("Burn", "7.62 L/lap"), ("Tank", "8.0 laps"), ("ETA", "02:38")]
    for (i, metric) in metrics.enumerated() {
        let x = 18 + CGFloat(i) * 150
        c.fill(CGRect(x: x, y: 205, width: 136, height: 46), Theme.panel, radius: 6)
        c.text(metric.0, CGRect(x: x + 10, y: 229, width: 116, height: 14), size: 10, color: Theme.muted)
        c.text(metric.1, CGRect(x: x + 10, y: 209, width: 116, height: 20), size: 14, weight: .bold)
    }
    header(c, y: 166, columns: [("STINT", 22, 72), ("LAPS", 120, 60), ("ADD", 206, 76), ("TARGET", 306, 92), ("NOTE", 430, 160)])
    let rows = [("1", "7/8", "48.0 L", "7.50", "save 0.12 L/lap"), ("2", "8", "60.8 L", "7.60", "target"), ("3", "7", "54.0 L", "7.65", "short fill")]
    for (i, row) in rows.enumerated() {
        let y = 124 - CGFloat(i) * 38
        c.fill(CGRect(x: 18, y: y, width: 604, height: 30), i == 0 ? Theme.infoBackground : Theme.panel, radius: 4)
        c.text(row.0, CGRect(x: 36, y: y + 8, width: 40, height: 14), size: 11, weight: .bold)
        c.text(row.1, CGRect(x: 124, y: y + 8, width: 60, height: 14), size: 11, weight: .bold)
        c.text(row.2, CGRect(x: 210, y: y + 8, width: 76, height: 14), size: 11)
        c.text(row.3, CGRect(x: 316, y: y + 8, width: 76, height: 14), size: 11, color: Theme.success)
        c.text(row.4, CGRect(x: 434, y: y + 8, width: 170, height: 14), size: 11, color: i == 0 ? Theme.warning : Theme.muted)
    }
}

private func renderInputs(_ c: Canvas) {
    renderBase(c, title: "Input Telemetry", status: "LIVE")
    let bars = [("THR", 0.86, Theme.success), ("BRK", 0.22, Theme.error), ("CLT", 0.00, Theme.muted), ("STR", 0.58, Theme.info)]
    for (i, bar) in bars.enumerated() {
        let y = 200 - CGFloat(i) * 42
        c.text(bar.0, CGRect(x: 24, y: y + 6, width: 42, height: 16), size: 12, weight: .bold, color: Theme.muted)
        c.bar(CGRect(x: 76, y: y + 6, width: 350, height: 16), value: CGFloat(bar.1), color: bar.2)
        c.text("\(Int(bar.1 * 100))%", CGRect(x: 432, y: y + 4, width: 40, height: 16), size: 11, color: bar.2, align: .right)
    }
    for i in 0..<12 {
        c.fill(CGRect(x: 78 + CGFloat(i) * 28, y: 48, width: 18, height: 16), i < 9 ? Theme.success : Theme.panelAlt, radius: 2)
    }
}

private func renderTrackMap(_ c: Canvas) {
    renderBase(c, title: "Track Map", status: "N24")
    drawTrack(c, rect: CGRect(x: 54, y: 54, width: 372, height: 340), closed: true)
    carDot(c, x: 260, y: 300, color: Theme.success, label: "TMR")
    carDot(c, x: 322, y: 230, color: Theme.pink, label: "+1")
    carDot(c, x: 148, y: 168, color: Theme.cyan, label: "GTP")
}

private func renderFlatMap(_ c: Canvas) {
    renderBase(c, title: "Flat Map", status: "TRAFFIC")
    let y: CGFloat = 126
    c.line(from: CGPoint(x: 30, y: y), to: CGPoint(x: 590, y: y + 28), color: Theme.track, width: 14)
    c.line(from: CGPoint(x: 30, y: y), to: CGPoint(x: 590, y: y + 28), color: Theme.border, width: 2)
    for i in 0..<9 {
        let x = 54 + CGFloat(i) * 62
        c.text("T\(i + 1)", CGRect(x: x - 10, y: y + 38, width: 32, height: 14), size: 9, color: Theme.muted, align: .center)
    }
    carDot(c, x: 334, y: y + 16, color: Theme.success, label: "YOU")
    carDot(c, x: 398, y: y + 20, color: Theme.pink, label: "+0.8")
    carDot(c, x: 220, y: y + 9, color: Theme.cyan, label: "FAST")
}

private func renderMiniMap(_ c: Canvas) {
    renderBase(c, title: "Mini Map")
    drawTrack(c, rect: CGRect(x: 52, y: 44, width: 196, height: 190), closed: true)
    carDot(c, x: 168, y: 190, color: Theme.success, label: "")
    carDot(c, x: 112, y: 112, color: Theme.pink, label: "")
}

private func renderRadar(_ c: Canvas) {
    renderBase(c, title: "Radar", status: "CLEAR")
    let center = CGPoint(x: 180, y: 170)
    for r in stride(from: CGFloat(48), through: 126, by: 39) {
        c.circle(center: center, radius: r, color: NSColor(white: 1, alpha: 0.10), width: 1)
    }
    c.line(from: CGPoint(x: center.x, y: 40), to: CGPoint(x: center.x, y: 300), color: NSColor(white: 1, alpha: 0.09))
    c.line(from: CGPoint(x: 50, y: center.y), to: CGPoint(x: 310, y: center.y), color: NSColor(white: 1, alpha: 0.09))
    c.fill(CGRect(x: center.x - 14, y: center.y - 26, width: 28, height: 52), Theme.success, radius: 4)
    c.fill(CGRect(x: center.x + 58, y: center.y - 16, width: 22, height: 42), Theme.error, radius: 4)
    c.fill(CGRect(x: center.x - 86, y: center.y + 22, width: 22, height: 42), Theme.warning, radius: 4)
}

private func renderRadarBars(_ c: Canvas) {
    renderBase(c, title: "Radar Bars", status: "SIDE")
    c.fill(CGRect(x: 34, y: 58, width: 110, height: 64), Theme.warningBackground, radius: 7)
    c.centered("LEFT 0.3s", CGRect(x: 34, y: 58, width: 110, height: 64), size: 16, color: Theme.warning)
    c.fill(CGRect(x: 190, y: 58, width: 140, height: 64), Theme.successBackground, radius: 7)
    c.centered("TMR", CGRect(x: 190, y: 58, width: 140, height: 64), size: 16, color: Theme.success)
    c.fill(CGRect(x: 376, y: 58, width: 110, height: 64), Theme.panel, radius: 7)
    c.centered("RIGHT", CGRect(x: 376, y: 58, width: 110, height: 64), size: 16, color: Theme.muted)
}

private func renderBlindSpot(_ c: Canvas) {
    renderBase(c, title: "Blind Spot", status: "WARNING")
    c.fill(CGRect(x: 22, y: 52, width: 150, height: 68), Theme.errorBackground, radius: 8)
    c.centered("CAR LEFT", CGRect(x: 22, y: 52, width: 150, height: 68), size: 18, color: Theme.error)
    c.fill(CGRect(x: 194, y: 64, width: 132, height: 44), Theme.successBackground, radius: 6)
    c.centered("CLEAR AHEAD", CGRect(x: 194, y: 64, width: 132, height: 44), size: 12, color: Theme.success)
    c.fill(CGRect(x: 348, y: 52, width: 150, height: 68), Theme.panel, radius: 8)
    c.centered("RIGHT CLEAR", CGRect(x: 348, y: 52, width: 150, height: 68), size: 14, color: Theme.muted)
}

private func renderOvertakeAlert(_ c: Canvas) {
    renderBase(c, title: "Multiclass Traffic", status: "GTP")
    c.fill(CGRect(x: 18, y: 54, width: 464, height: 58), Theme.warningBackground, radius: 8)
    c.text("FASTER CLASS APPROACHING", CGRect(x: 36, y: 88, width: 300, height: 18), size: 13, weight: .bold, color: Theme.warning)
    c.text("Porsche 963 - closing 1.8s/lap - pass in 42s", CGRect(x: 36, y: 66, width: 410, height: 16), size: 11, color: Theme.secondary)
}

private func renderPitBox(_ c: Canvas) {
    renderBase(c, title: "Pit Box Helper", status: "PIT IN")
    c.fill(CGRect(x: 28, y: 72, width: 504, height: 36), Theme.panel, radius: 4)
    c.fill(CGRect(x: 342, y: 68, width: 56, height: 44), Theme.successBackground, radius: 5)
    c.centered("BOX", CGRect(x: 342, y: 68, width: 56, height: 44), size: 13, color: Theme.success)
    c.text("Brake marker 90m", CGRect(x: 34, y: 126, width: 160, height: 16), size: 12, color: Theme.warning)
    c.text("Roll 28 kph", CGRect(x: 410, y: 126, width: 110, height: 16), size: 12, color: Theme.info, align: .right)
    c.line(from: CGPoint(x: 34, y: 90), to: CGPoint(x: 520, y: 90), color: Theme.border, width: 2)
}

private func renderRaceControl(_ c: Canvas) {
    renderBase(c, title: "Race Control", status: "4 EVENTS")
    header(c, y: 350, columns: [("TIME", 24, 70), ("TYPE", 116, 120), ("CAR", 258, 90), ("NOTE", 368, 280)])
    let rows = [("1:42:08", "YELLOW", "#24", "Sector 2 slow car on right"), ("1:45:32", "INCIDENT", "#87", "Contact at Aremberg"), ("1:49:11", "PIT EXIT", "#12", "Unsafe blend warning"), ("1:52:04", "CLEAR", "-", "Green through sector 2")]
    for (i, row) in rows.enumerated() {
        let y = 304 - CGFloat(i) * 52
        let color = row.1 == "CLEAR" ? Theme.successBackground : row.1 == "YELLOW" ? Theme.warningBackground : Theme.errorBackground
        c.fill(CGRect(x: 18, y: y, width: 664, height: 40), i == 0 ? color : Theme.panel, radius: 4)
        c.text(row.0, CGRect(x: 30, y: y + 13, width: 80, height: 16), size: 11, color: Theme.secondary)
        c.text(row.1, CGRect(x: 118, y: y + 13, width: 120, height: 16), size: 11, weight: .bold, color: row.1 == "CLEAR" ? Theme.success : row.1 == "YELLOW" ? Theme.warning : Theme.error)
        c.text(row.2, CGRect(x: 266, y: y + 13, width: 60, height: 16), size: 11)
        c.text(row.3, CGRect(x: 374, y: y + 13, width: 280, height: 16), size: 11, color: Theme.secondary)
    }
}

private func renderDataBlocks(_ c: Canvas) {
    renderBase(c, title: "Data Blocks", status: "CUSTOM")
    let blocks = [("Track", "22.7 C"), ("Air", "18.4 C"), ("Wind", "9 kph"), ("Brake", "73%"), ("Oil", "101 C"), ("Water", "88 C")]
    for (i, block) in blocks.enumerated() {
        let col = i % 3
        let row = i / 3
        let rect = CGRect(x: 20 + CGFloat(col) * 174, y: 126 - CGFloat(row) * 70, width: 154, height: 54)
        c.fill(rect, Theme.panel, radius: 6)
        c.text(block.0, CGRect(x: rect.minX + 12, y: rect.minY + 31, width: 120, height: 14), size: 10, color: Theme.muted)
        c.text(block.1, CGRect(x: rect.minX + 12, y: rect.minY + 10, width: 120, height: 20), size: 16, weight: .bold)
    }
}

private func renderDelta(_ c: Canvas) {
    renderBase(c, title: "Delta", status: "BEST")
    c.text("-0.184", CGRect(x: 36, y: 62, width: 180, height: 50), size: 38, weight: .bold, color: Theme.success)
    c.text("vs session best", CGRect(x: 42, y: 46, width: 140, height: 14), size: 10, color: Theme.muted)
    c.bar(CGRect(x: 232, y: 78, width: 250, height: 16), value: 0.62, color: Theme.success)
}

private func renderDigiFlag(_ c: Canvas) {
    renderBase(c, title: "Digital Flag")
    c.fill(CGRect(x: 24, y: 48, width: 292, height: 78), Theme.warningBackground, radius: 8)
    c.centered("LOCAL YELLOW", CGRect(x: 24, y: 48, width: 292, height: 78), size: 28, color: Theme.warning)
}

private func renderFlags(_ c: Canvas) {
    renderBase(c, title: "Flags", status: "S2")
    let flags = [("GREEN", Theme.successBackground, Theme.success), ("YELLOW", Theme.warningBackground, Theme.warning), ("BLUE", Theme.infoBackground, Theme.info), ("MEATBALL", Theme.errorBackground, Theme.error)]
    for (i, flag) in flags.enumerated() {
        let rect = CGRect(x: 20 + CGFloat(i) * 132, y: 58, width: 116, height: 64)
        c.fill(rect, flag.1, radius: 6)
        c.centered(flag.0, rect, size: 13, color: flag.2)
    }
}

private func renderBoost(_ c: Canvas) {
    renderBase(c, title: "Boost Box", status: "LMDh")
    c.text("Deploy", CGRect(x: 28, y: 138, width: 90, height: 18), size: 12, color: Theme.muted)
    c.bar(CGRect(x: 28, y: 112, width: 300, height: 18), value: 0.72, color: Theme.info)
    c.text("72%", CGRect(x: 28, y: 74, width: 120, height: 32), size: 28, weight: .bold, color: Theme.info)
    c.text("regen +1.2 MJ", CGRect(x: 184, y: 84, width: 120, height: 18), size: 12, color: Theme.success, align: .right)
}

private func renderGForce(_ c: Canvas) {
    renderBase(c, title: "G-Force")
    let center = CGPoint(x: 170, y: 158)
    for r in stride(from: CGFloat(38), through: 104, by: 33) {
        c.circle(center: center, radius: r, color: NSColor(white: 1, alpha: 0.12), width: 1)
    }
    c.line(from: CGPoint(x: center.x - 112, y: center.y), to: CGPoint(x: center.x + 112, y: center.y), color: NSColor(white: 1, alpha: 0.12))
    c.line(from: CGPoint(x: center.x, y: center.y - 112), to: CGPoint(x: center.x, y: center.y + 112), color: NSColor(white: 1, alpha: 0.12))
    c.circle(center: CGPoint(x: center.x + 44, y: center.y - 28), radius: 10, color: Theme.success, fill: true)
    c.text("1.34g", CGRect(x: 122, y: 42, width: 96, height: 24), size: 20, weight: .bold, color: Theme.success, align: .center)
}

private func renderHeadToHead(_ c: Canvas) {
    renderBase(c, title: "Head To Head", status: "DUEL")
    c.fill(CGRect(x: 24, y: 100, width: 270, height: 72), Theme.successBackground, radius: 8)
    c.fill(CGRect(x: 346, y: 100, width: 270, height: 72), Theme.panel, radius: 8)
    c.text("TMR Team", CGRect(x: 42, y: 142, width: 160, height: 18), size: 14, weight: .bold, color: Theme.success)
    c.text("A. Moreau", CGRect(x: 364, y: 142, width: 160, height: 18), size: 14, weight: .bold)
    c.text("8:12.904", CGRect(x: 42, y: 116, width: 110, height: 18), size: 13)
    c.text("8:13.188", CGRect(x: 364, y: 116, width: 110, height: 18), size: 13)
    c.centered("+0.284", CGRect(x: 286, y: 116, width: 68, height: 40), size: 16, color: Theme.success)
}

private func renderHeartRate(_ c: Canvas) {
    renderBase(c, title: "Heart Rate", status: "LIVE")
    c.text("148", CGRect(x: 30, y: 86, width: 96, height: 44), size: 38, weight: .bold, color: Theme.error)
    c.text("bpm", CGRect(x: 122, y: 96, width: 44, height: 18), size: 12, color: Theme.muted)
    var points: [CGPoint] = []
    for i in 0..<9 {
        points.append(CGPoint(x: 172 + CGFloat(i) * 14, y: 96 + CGFloat([0, 18, -8, 12, -18, 22, -6, 8, -12][i])))
    }
    c.polyline(points, color: Theme.error, width: 2)
}

private func renderLapGraph(_ c: Canvas) {
    renderBase(c, title: "Lap Time Graph", status: "STINT")
    chartGrid(c, rect: CGRect(x: 44, y: 54, width: 560, height: 170))
    c.polyline(sampleLine(rect: CGRect(x: 44, y: 54, width: 560, height: 170), values: [0.72, 0.65, 0.60, 0.57, 0.53, 0.55, 0.50, 0.48]), color: Theme.success, width: 2)
    c.polyline(sampleLine(rect: CGRect(x: 44, y: 54, width: 560, height: 170), values: [0.84, 0.78, 0.74, 0.69, 0.66, 0.64, 0.62, 0.59]), color: Theme.pink, width: 2)
}

private func renderLapLog(_ c: Canvas) {
    renderBase(c, title: "Lap Time Log", status: "LAST 8")
    let times = ["8:13.428", "8:12.904", "8:13.002", "8:12.771", "8:14.120", "8:13.552"]
    for (i, time) in times.enumerated() {
        let y = 286 - CGFloat(i) * 38
        c.fill(CGRect(x: 18, y: y, width: 394, height: 30), i == 3 ? Theme.successBackground : Theme.panel, radius: 4)
        c.text("L\(31 + i)", CGRect(x: 34, y: y + 8, width: 48, height: 14), size: 11, color: Theme.muted)
        c.text(time, CGRect(x: 112, y: y + 7, width: 100, height: 16), size: 12, weight: .bold)
        c.text(i == 3 ? "BEST" : "+0.\(i + 1)2", CGRect(x: 314, y: y + 8, width: 70, height: 14), size: 11, color: i == 3 ? Theme.success : Theme.muted, align: .right)
    }
}

private func renderLapSpread(_ c: Canvas) {
    renderBase(c, title: "Lap Time Spread", status: "CLASS")
    chartGrid(c, rect: CGRect(x: 42, y: 60, width: 540, height: 142))
    for (i, row) in drivers.prefix(4).enumerated() {
        let y = 82 + CGFloat(i) * 28
        c.line(from: CGPoint(x: 80, y: y), to: CGPoint(x: 520 - CGFloat(i) * 35, y: y + CGFloat(i * 8)), color: row.color, width: 4)
        c.text(row.name, CGRect(x: 42, y: y - 7, width: 90, height: 14), size: 9, color: Theme.muted)
    }
}

private func renderRaceSchedule(_ c: Canvas) {
    renderBase(c, title: "Race Schedule", status: "TODAY")
    let rows = [("16:00", "IMSA Open", "Nurburgring", "GT3/GTP"), ("18:15", "GT Sprint", "Spa", "GT3"), ("20:00", "Endurance", "Sebring", "Multi"), ("21:45", "Practice", "Road Atlanta", "GT4")]
    for (i, row) in rows.enumerated() {
        let y = 254 - CGFloat(i) * 52
        c.fill(CGRect(x: 20, y: y, width: 580, height: 40), Theme.panel, radius: 5)
        c.text(row.0, CGRect(x: 34, y: y + 13, width: 58, height: 16), size: 11, weight: .bold, color: Theme.info)
        c.text(row.1, CGRect(x: 112, y: y + 13, width: 140, height: 16), size: 11, weight: .semibold)
        c.text(row.2, CGRect(x: 282, y: y + 13, width: 140, height: 16), size: 11, color: Theme.secondary)
        c.text(row.3, CGRect(x: 500, y: y + 13, width: 72, height: 16), size: 11, color: Theme.muted, align: .right)
    }
}

private func renderSessionTimer(_ c: Canvas) {
    renderBase(c, title: "Session Timer", status: "RACE")
    c.text("02:18:44", CGRect(x: 28, y: 54, width: 190, height: 44), size: 34, weight: .bold)
    c.text("/ 04:00:00", CGRect(x: 226, y: 66, width: 116, height: 22), size: 16, color: Theme.muted)
    c.bar(CGRect(x: 28, y: 38, width: 374, height: 8), value: 0.58, color: Theme.success)
}

private func renderWeather(_ c: Canvas) {
    renderBase(c, title: "Weather Monitor", status: "DRYING")
    let blocks = [("Track", "27.4 C", Theme.warning), ("Air", "19.2 C", Theme.info), ("Wetness", "12%", Theme.success), ("Wind", "11 kph", Theme.muted)]
    for (i, block) in blocks.enumerated() {
        let x = 20 + CGFloat(i % 2) * 260
        let y = 144 - CGFloat(i / 2) * 72
        c.fill(CGRect(x: x, y: y, width: 238, height: 54), Theme.panel, radius: 6)
        c.text(block.0, CGRect(x: x + 12, y: y + 32, width: 100, height: 14), size: 10, color: Theme.muted)
        c.text(block.1, CGRect(x: x + 12, y: y + 10, width: 150, height: 20), size: 16, weight: .bold, color: block.2)
    }
}

private func renderTwitchChat(_ c: Canvas) {
    renderBase(c, title: "Twitch Chat", status: "STREAM")
    let messages = [("lapbot", "Traffic behind after Hohe Acht."), ("crew", "Box this lap, fuel only."), ("viewer42", "Great save through sector 2."), ("spotter", "Clear right.")]
    for (i, msg) in messages.enumerated() {
        let y = 232 - CGFloat(i) * 50
        c.fill(CGRect(x: 18, y: y, width: 394, height: 38), Theme.panel, radius: 5)
        c.text(msg.0, CGRect(x: 30, y: y + 18, width: 90, height: 14), size: 10, weight: .bold, color: Theme.info)
        c.text(msg.1, CGRect(x: 30, y: y + 5, width: 350, height: 14), size: 10, color: Theme.secondary)
    }
}

private func renderGarageCover(_ c: Canvas) {
    renderBase(c, title: "Garage Cover", status: "PRIVATE")
    c.fill(CGRect(x: 32, y: 54, width: 576, height: 250), Theme.panel, radius: 8)
    c.centered("SETUP HIDDEN", CGRect(x: 32, y: 176, width: 576, height: 54), size: 34, color: Theme.secondary)
    c.centered("TmrOverlay masks garage/setup data while streaming", CGRect(x: 32, y: 146, width: 576, height: 30), size: 13, color: Theme.muted)
    c.fill(CGRect(x: 96, y: 94, width: 448, height: 10), NSColor(white: 1, alpha: 0.08), radius: 5)
}

private func header(_ c: Canvas, y: CGFloat, columns: [(String, CGFloat, CGFloat)]) {
    c.fill(CGRect(x: 18, y: y, width: c.size.width - 36, height: 24), Theme.panelAlt, radius: 4)
    for column in columns {
        c.text(column.0, CGRect(x: column.1, y: y + 6, width: column.2, height: 12), size: 9, weight: .bold, color: Theme.muted)
    }
}

private func tableRows(_ c: Canvas, y: CGFloat, rowHeight: CGFloat, rows: [DriverRow]) {
    for (index, row) in rows.enumerated() {
        let rowY = y - CGFloat(index) * rowHeight
        c.fill(CGRect(x: 18, y: rowY, width: c.size.width - 36, height: rowHeight - 8), row.highlight ? Theme.successBackground : Theme.panel, radius: 4)
        c.fill(CGRect(x: 22, y: rowY + 8, width: 4, height: rowHeight - 24), row.color, radius: 2)
        c.text(row.position, CGRect(x: 38, y: rowY + 13, width: 34, height: 18), size: 14, weight: .bold)
        c.text(row.name, CGRect(x: 74, y: rowY + 14, width: 250, height: 18), size: 13, weight: .semibold)
        c.text(row.cls, CGRect(x: 350, y: rowY + 14, width: 52, height: 18), size: 12, color: row.color)
        c.text(row.gap, CGRect(x: 424, y: rowY + 14, width: 80, height: 18), size: 12, weight: .bold, color: row.highlight ? Theme.success : Theme.secondary)
        c.text(row.last, CGRect(x: 526, y: rowY + 14, width: 90, height: 18), size: 12)
        c.text(row.badge, CGRect(x: 620, y: rowY + 14, width: 74, height: 18), size: 11, weight: .bold, color: row.badge == "OUT" ? Theme.warning : Theme.muted, align: .right)
    }
}

private func drawTrack(_ c: Canvas, rect: CGRect, closed: Bool) {
    let points = [
        CGPoint(x: rect.minX + rect.width * 0.18, y: rect.minY + rect.height * 0.68),
        CGPoint(x: rect.minX + rect.width * 0.30, y: rect.minY + rect.height * 0.90),
        CGPoint(x: rect.minX + rect.width * 0.62, y: rect.minY + rect.height * 0.84),
        CGPoint(x: rect.minX + rect.width * 0.84, y: rect.minY + rect.height * 0.60),
        CGPoint(x: rect.minX + rect.width * 0.70, y: rect.minY + rect.height * 0.34),
        CGPoint(x: rect.minX + rect.width * 0.42, y: rect.minY + rect.height * 0.18),
        CGPoint(x: rect.minX + rect.width * 0.16, y: rect.minY + rect.height * 0.34)
    ]
    c.polyline(points + (closed ? [points[0]] : []), color: NSColor(red255: 72, green: 83, blue: 90), width: 20)
    c.polyline(points + (closed ? [points[0]] : []), color: Theme.border, width: 2)
}

private func carDot(_ c: Canvas, x: CGFloat, y: CGFloat, color: NSColor, label: String) {
    c.circle(center: CGPoint(x: x, y: y), radius: 7, color: color, fill: true)
    if !label.isEmpty {
        c.text(label, CGRect(x: x + 10, y: y - 6, width: 54, height: 14), size: 9, weight: .bold, color: color)
    }
}

private func chartGrid(_ c: Canvas, rect: CGRect) {
    c.fill(rect, NSColor(red255: 10, green: 13, blue: 16, alpha: 0.55), radius: 4)
    for i in 0...4 {
        let y = rect.minY + CGFloat(i) * rect.height / 4
        c.line(from: CGPoint(x: rect.minX, y: y), to: CGPoint(x: rect.maxX, y: y), color: NSColor(white: 1, alpha: 0.08))
    }
    for i in 0...6 {
        let x = rect.minX + CGFloat(i) * rect.width / 6
        c.line(from: CGPoint(x: x, y: rect.minY), to: CGPoint(x: x, y: rect.maxY), color: NSColor(white: 1, alpha: 0.06))
    }
}

private func sampleLine(rect: CGRect, values: [CGFloat]) -> [CGPoint] {
    values.enumerated().map { index, value in
        CGPoint(
            x: rect.minX + CGFloat(index) * rect.width / CGFloat(max(values.count - 1, 1)),
            y: rect.minY + (1 - value) * rect.height
        )
    }
}

private func renderPNG(size: CGSize, render: (Canvas) -> Void, to url: URL) throws {
    let rep = NSBitmapImageRep(
        bitmapDataPlanes: nil,
        pixelsWide: Int(size.width),
        pixelsHigh: Int(size.height),
        bitsPerSample: 8,
        samplesPerPixel: 4,
        hasAlpha: true,
        isPlanar: false,
        colorSpaceName: .deviceRGB,
        bytesPerRow: 0,
        bitsPerPixel: 0
    )!
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
    let context = NSGraphicsContext.current!.cgContext
    context.clear(CGRect(origin: .zero, size: size))
    render(Canvas(size: size, context: context))
    NSGraphicsContext.restoreGraphicsState()

    guard let data = rep.representation(using: .png, properties: [:]) else {
        throw NSError(domain: "render_overlay_catalog", code: 1, userInfo: [NSLocalizedDescriptionKey: "Could not encode PNG"])
    }
    try data.write(to: url)
}

private func renderCatalog(to outputRoot: URL) throws {
    try FileManager.default.createDirectory(at: outputRoot, withIntermediateDirectories: true)
    for spec in overlaySpecs {
        let url = outputRoot.appendingPathComponent("\(spec.slug).png")
        try renderPNG(size: spec.size, render: spec.render, to: url)
        print("wrote \(url.path)")
    }

    let thumbWidth: CGFloat = 300
    let thumbHeight: CGFloat = 190
    let cols = 4
    let rows = Int(ceil(Double(overlaySpecs.count) / Double(cols)))
    let sheetSize = CGSize(width: CGFloat(cols) * thumbWidth + 30, height: CGFloat(rows) * thumbHeight + 86)
    let sheetURL = outputRoot.appendingPathComponent("overlay-catalog.png")
    try renderPNG(size: sheetSize, render: { c in
        c.fill(CGRect(origin: .zero, size: sheetSize), NSColor(red255: 11, green: 14, blue: 17, alpha: 1.0))
        c.text("TmrOverlay Overlay Catalog", CGRect(x: 18, y: sheetSize.height - 42, width: 520, height: 28), size: 22, weight: .bold)
        c.text("Static design previews for RaceLab/iOverlay-style overlay kinds rendered with current TmrOverlay tokens.", CGRect(x: 18, y: sheetSize.height - 64, width: 780, height: 18), size: 11, color: Theme.muted)
        for (index, spec) in overlaySpecs.enumerated() {
            let col = index % cols
            let row = index / cols
            let x = 18 + CGFloat(col) * thumbWidth
            let y = sheetSize.height - 84 - CGFloat(row + 1) * thumbHeight
            c.fill(CGRect(x: x, y: y, width: thumbWidth - 18, height: thumbHeight - 16), Theme.panel, radius: 7)
            c.text(spec.title, CGRect(x: x + 12, y: y + thumbHeight - 42, width: thumbWidth - 42, height: 18), size: 12, weight: .bold)
            let inner = CGRect(x: x + 12, y: y + 14, width: thumbWidth - 42, height: thumbHeight - 62)
            let scale = min(inner.width / spec.size.width, inner.height / spec.size.height)
            let renderedSize = CGSize(width: spec.size.width * scale, height: spec.size.height * scale)
            let origin = CGPoint(x: inner.midX - renderedSize.width / 2, y: inner.midY - renderedSize.height / 2)
            c.context.saveGState()
            c.context.translateBy(x: origin.x, y: origin.y)
            c.context.scaleBy(x: scale, y: scale)
            spec.render(Canvas(size: spec.size, context: c.context))
            c.context.restoreGState()
        }
    }, to: sheetURL)
    print("wrote \(sheetURL.path)")
}

let output = CommandLine.arguments.dropFirst().first.map(URL.init(fileURLWithPath:))
    ?? URL(fileURLWithPath: FileManager.default.currentDirectoryPath)
        .appendingPathComponent("mocks/overlay-catalog", isDirectory: true)

do {
    try renderCatalog(to: output)
} catch {
    fputs("Failed to render overlay catalog: \(error)\n", stderr)
    exit(1)
}
