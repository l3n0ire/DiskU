import Foundation
import Testing
@testable import DiskUKit

private func file(_ name: String, _ size: Int64) -> FileNode {
    FileNode(url: URL(fileURLWithPath: "/r/\(name)"), kind: Kind.forFile(named: name), size: size, itemCount: 1)
}

private func folder(_ name: String, _ children: [FileNode]) -> FileNode {
    let node = FileNode(url: URL(fileURLWithPath: "/r/\(name)"), kind: .folder)
    node.adopt(children)
    return node
}

@Test func adoptSumsAndSortsChildren() {
    let root = folder("root", [file("a.txt", 10), folder("sub", [file("b.mp4", 30), file("c.png", 50)]), file("d.zip", 40)])
    #expect(root.size == 130)
    #expect(root.itemCount == 4)
    #expect(root.children.map(\.name) == ["sub", "d.zip", "a.txt"])
    #expect(root.children[0].children.map(\.name) == ["c.png", "b.mp4"])
}

@Test func removingNodeUpdatesEveryAncestorAndResorts() {
    let big = file("big.mov", 100)
    let inner = folder("inner", [big, file("small.txt", 5)])
    let mid = folder("mid", [inner, file("m.png", 20)])
    let root = folder("root", [mid, file("other.zip", 60)])
    #expect(root.size == 185)
    #expect(root.children.map(\.name) == ["mid", "other.zip"])

    big.removeFromParent()

    #expect(big.parent == nil)
    #expect(inner.size == 5)
    #expect(mid.size == 25)
    #expect(root.size == 85)
    #expect(root.itemCount == 3)
    #expect(mid.children.map(\.name) == ["m.png", "inner"])
    #expect(root.children.map(\.name) == ["other.zip", "mid"])
}

@Test func kindRegistryMapsExtensions() {
    #expect(Kind.forFile(named: "Clip.MOV") == .video)
    #expect(Kind.forFile(named: "x.tar.gz") == .archive)
    #expect(Kind.forFile(named: "main.swift") == .code)
    #expect(Kind.forFile(named: "README") == .other)
}
