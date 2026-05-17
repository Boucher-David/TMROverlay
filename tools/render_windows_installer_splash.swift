import AppKit
import Foundation

private enum SplashTheme {
    static let background = NSColor(calibratedRed: 3 / 255, green: 11 / 255, blue: 24 / 255, alpha: 1)
    static let content = NSColor(calibratedRed: 248 / 255, green: 250 / 255, blue: 252 / 255, alpha: 1)
    static let contentLine = NSColor(calibratedRed: 205 / 255, green: 216 / 255, blue: 228 / 255, alpha: 1)
    static let accent = NSColor(calibratedRed: 0 / 255, green: 232 / 255, blue: 255 / 255, alpha: 1)
    static let magenta = NSColor(calibratedRed: 255 / 255, green: 42 / 255, blue: 167 / 255, alpha: 1)
}

private final class SplashCanvas {
    let size: CGSize

    init(size: CGSize) {
        self.size = size
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

    private func path(_ rect: CGRect, _ radius: CGFloat) -> NSBezierPath {
        radius <= 0 ? NSBezierPath(rect: rect) : NSBezierPath(roundedRect: rect, xRadius: radius, yRadius: radius)
    }
}

private func renderSplash(_ canvas: SplashCanvas, logo: NSImage?) {
    let bounds = CGRect(origin: .zero, size: canvas.size)
    let railWidth = CGFloat(192)
    let rail = CGRect(x: bounds.minX, y: bounds.minY, width: railWidth, height: bounds.height)
    let content = CGRect(x: rail.maxX, y: bounds.minY, width: bounds.width - railWidth, height: bounds.height)

    canvas.fill(bounds, SplashTheme.content)
    canvas.fill(rail, SplashTheme.background)
    canvas.fill(content, SplashTheme.content)

    for x in stride(from: CGFloat(0), through: rail.maxX, by: 48) {
        canvas.line(from: CGPoint(x: x, y: 0), to: CGPoint(x: x, y: bounds.height), color: NSColor(calibratedRed: 0 / 255, green: 232 / 255, blue: 255 / 255, alpha: 0.13))
    }
    for y in stride(from: CGFloat(0), through: bounds.height, by: 64) {
        canvas.line(from: CGPoint(x: 0, y: y), to: CGPoint(x: rail.maxX, y: y), color: NSColor(calibratedRed: 255 / 255, green: 42 / 255, blue: 167 / 255, alpha: 0.11))
    }

    canvas.fill(CGRect(x: rail.maxX, y: bounds.minY, width: 2, height: bounds.height), SplashTheme.magenta)
    canvas.fill(CGRect(x: rail.maxX + 2, y: bounds.minY, width: 2, height: bounds.height), SplashTheme.accent)
    canvas.fill(CGRect(x: content.minX + 40, y: 69, width: content.width - 80, height: 1), SplashTheme.contentLine)
    canvas.fill(CGRect(x: content.minX + 40, y: 109, width: content.width - 146, height: 1), SplashTheme.contentLine)
    canvas.fill(CGRect(x: content.minX + 40, y: 149, width: content.width - 190, height: 1), SplashTheme.contentLine)
    canvas.fill(CGRect(x: content.minX + 40, y: 233, width: 130, height: 5), SplashTheme.magenta)
    canvas.fill(CGRect(x: content.minX + 170, y: 233, width: 190, height: 5), SplashTheme.accent)

    if let logo {
        logo.draw(in: CGRect(x: 30, y: 244, width: 132, height: 74), from: .zero, operation: .sourceOver, fraction: 1)
    }
}

private func findRepositoryRoot(startingAt url: URL) -> URL? {
    var directory = url.standardizedFileURL
    while true {
        if FileManager.default.fileExists(atPath: directory.appendingPathComponent("tmrOverlay.sln").path) {
            return directory
        }

        let parent = directory.deletingLastPathComponent()
        if parent.path == directory.path {
            return nil
        }

        directory = parent
    }
}

private func renderMsiBanner(_ canvas: SplashCanvas, logo: NSImage?) {
    let bounds = CGRect(origin: .zero, size: canvas.size)
    canvas.fill(bounds, SplashTheme.content)
    canvas.fill(CGRect(x: bounds.minX, y: bounds.minY, width: bounds.width, height: 1), SplashTheme.contentLine)
    canvas.fill(CGRect(x: bounds.minX, y: bounds.maxY - 5, width: bounds.width, height: 3), SplashTheme.magenta)
    canvas.fill(CGRect(x: bounds.minX, y: bounds.maxY - 2, width: bounds.width, height: 2), SplashTheme.accent)

    canvas.fill(CGRect(x: bounds.maxX - 86, y: 11, width: 54, height: 5), SplashTheme.magenta)
    canvas.fill(CGRect(x: bounds.maxX - 66, y: 21, width: 34, height: 5), SplashTheme.accent)
    canvas.fill(CGRect(x: bounds.maxX - 104, y: 31, width: 72, height: 5), SplashTheme.contentLine)
}

private func renderMsiLogo(_ canvas: SplashCanvas, logo: NSImage?) {
    let bounds = CGRect(origin: .zero, size: canvas.size)
    let railWidth = CGFloat(166)
    let rail = CGRect(x: bounds.minX, y: bounds.minY, width: railWidth, height: bounds.height)
    let content = CGRect(x: rail.maxX, y: bounds.minY, width: bounds.width - railWidth, height: bounds.height)

    canvas.fill(bounds, SplashTheme.content)
    canvas.fill(rail, SplashTheme.background)
    canvas.fill(content, SplashTheme.content)

    for x in stride(from: CGFloat(0), through: rail.maxX, by: 42) {
        canvas.line(from: CGPoint(x: x, y: 0), to: CGPoint(x: x, y: bounds.height), color: NSColor(calibratedRed: 0 / 255, green: 232 / 255, blue: 255 / 255, alpha: 0.13))
    }
    for y in stride(from: CGFloat(0), through: bounds.height, by: 56) {
        canvas.line(from: CGPoint(x: 0, y: y), to: CGPoint(x: rail.maxX, y: y), color: NSColor(calibratedRed: 255 / 255, green: 42 / 255, blue: 167 / 255, alpha: 0.11))
    }

    canvas.fill(CGRect(x: rail.maxX, y: bounds.minY, width: 2, height: bounds.height), SplashTheme.magenta)
    canvas.fill(CGRect(x: rail.maxX + 2, y: bounds.minY, width: 2, height: bounds.height), SplashTheme.accent)
    canvas.fill(CGRect(x: content.minX + 28, y: 54, width: content.width - 56, height: 1), SplashTheme.contentLine)
    canvas.fill(CGRect(x: content.minX + 28, y: 91, width: content.width - 102, height: 1), SplashTheme.contentLine)
    canvas.fill(CGRect(x: content.minX + 28, y: 128, width: content.width - 136, height: 1), SplashTheme.contentLine)

    if let logo {
        logo.draw(in: CGRect(x: 25, y: 199, width: 116, height: 65), from: .zero, operation: .sourceOver, fraction: 1)
    }
}

private func renderImage(
    size: CGSize,
    type: NSBitmapImageRep.FileType,
    to url: URL,
    logo: NSImage?,
    render: (SplashCanvas, NSImage?) -> Void
) throws {
    guard let rep = NSBitmapImageRep(
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
    ) else {
        throw NSError(domain: "render_windows_installer_splash", code: 1)
    }

    NSGraphicsContext.saveGraphicsState()
    let graphicsContext = NSGraphicsContext(bitmapImageRep: rep)!
    NSGraphicsContext.current = graphicsContext
    graphicsContext.cgContext.clear(CGRect(origin: .zero, size: size))
    render(SplashCanvas(size: size), logo)
    NSGraphicsContext.restoreGraphicsState()

    if type == .bmp {
        try writeBmp24(rep, to: url)
        return
    }

    guard let data = rep.representation(using: type, properties: [:]) else {
        throw NSError(domain: "render_windows_installer_splash", code: 2)
    }

    try data.write(to: url)
}

private func writeBmp24(_ rep: NSBitmapImageRep, to url: URL) throws {
    let width = rep.pixelsWide
    let height = rep.pixelsHigh
    let rowStride = ((width * 3 + 3) / 4) * 4
    let imageSize = rowStride * height
    let fileSize = 54 + imageSize
    var data = Data(capacity: fileSize)

    appendUInt16LE(0x4D42, to: &data)
    appendUInt32LE(UInt32(fileSize), to: &data)
    appendUInt16LE(0, to: &data)
    appendUInt16LE(0, to: &data)
    appendUInt32LE(54, to: &data)
    appendUInt32LE(40, to: &data)
    appendInt32LE(Int32(width), to: &data)
    appendInt32LE(-Int32(height), to: &data)
    appendUInt16LE(1, to: &data)
    appendUInt16LE(24, to: &data)
    appendUInt32LE(0, to: &data)
    appendUInt32LE(UInt32(imageSize), to: &data)
    appendInt32LE(0, to: &data)
    appendInt32LE(0, to: &data)
    appendUInt32LE(0, to: &data)
    appendUInt32LE(0, to: &data)

    for y in 0..<height {
        var row = Data(capacity: rowStride)
        for x in 0..<width {
            let color = rep.colorAt(x: x, y: y)?.usingColorSpace(.deviceRGB)
                ?? NSColor.black
            row.append(UInt8(clamping: Int((color.blueComponent * 255).rounded())))
            row.append(UInt8(clamping: Int((color.greenComponent * 255).rounded())))
            row.append(UInt8(clamping: Int((color.redComponent * 255).rounded())))
        }

        while row.count < rowStride {
            row.append(0)
        }
        data.append(row)
    }

    try data.write(to: url)
}

private func appendUInt16LE(_ value: UInt16, to data: inout Data) {
    var littleEndian = value.littleEndian
    withUnsafeBytes(of: &littleEndian) { bytes in
        data.append(contentsOf: bytes)
    }
}

private func appendUInt32LE(_ value: UInt32, to data: inout Data) {
    var littleEndian = value.littleEndian
    withUnsafeBytes(of: &littleEndian) { bytes in
        data.append(contentsOf: bytes)
    }
}

private func appendInt32LE(_ value: Int32, to data: inout Data) {
    var littleEndian = value.littleEndian
    withUnsafeBytes(of: &littleEndian) { bytes in
        data.append(contentsOf: bytes)
    }
}

let currentDirectory = URL(fileURLWithPath: FileManager.default.currentDirectoryPath)
let repositoryRoot = findRepositoryRoot(startingAt: currentDirectory) ?? currentDirectory
let logo = NSImage(contentsOf: repositoryRoot.appendingPathComponent("assets/brand/TMRLogo.png"))
let outputDirectory = CommandLine.arguments.dropFirst().first.map(URL.init(fileURLWithPath:))
    ?? repositoryRoot.appendingPathComponent("assets/brand")

do {
    try FileManager.default.createDirectory(at: outputDirectory, withIntermediateDirectories: true)
    let outputs: [(URL, CGSize, NSBitmapImageRep.FileType, (SplashCanvas, NSImage?) -> Void)] = [
        (
            outputDirectory.appendingPathComponent("TMRInstallerSplash.png"),
            CGSize(width: 640, height: 400),
            .png,
            renderSplash
        ),
        (
            outputDirectory.appendingPathComponent("TMRMsiBanner.bmp"),
            CGSize(width: 493, height: 58),
            .bmp,
            renderMsiBanner
        ),
        (
            outputDirectory.appendingPathComponent("TMRMsiLogo.bmp"),
            CGSize(width: 493, height: 312),
            .bmp,
            renderMsiLogo
        )
    ]

    for (url, size, type, render) in outputs {
        try renderImage(size: size, type: type, to: url, logo: logo, render: render)
        print("wrote \(url.path)")
    }
} catch {
    fputs("Failed to render installer artwork: \(error)\n", stderr)
    exit(1)
}
