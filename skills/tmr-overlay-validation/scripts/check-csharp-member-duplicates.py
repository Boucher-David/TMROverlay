#!/usr/bin/env python3
"""Detect C# compile hazards that are easy to miss without dotnet."""

from __future__ import annotations

import argparse
import bisect
import re
import sys
from dataclasses import dataclass
from pathlib import Path


TYPE_DECL_RE = re.compile(
    r"\b(?P<kind>record(?:\s+(?:class|struct))?|class|struct|interface)\s+"
    r"(?P<name>@?[A-Za-z_][A-Za-z0-9_]*)"
)

IDENT_RE = re.compile(r"@?[A-Za-z_][A-Za-z0-9_]*")
VAR_DECL_RE = re.compile(r"\bvar\s+(?P<name>@?[A-Za-z_][A-Za-z0-9_]*)\s*=")
MEMBER_ACCESS_RE = re.compile(
    r"\b(?P<type>[A-Za-z_][A-Za-z0-9_]*)\s*\.\s*(?P<member>[A-Za-z_][A-Za-z0-9_]*)"
)
NULLABLE_CAST_RE = re.compile(r"^\(\s*[^()]+\?\s*\)")
NUMERIC_LITERAL_RE = re.compile(r"\b\d+(?:\.\d+)?(?:d|f|m|l|ul|u)?\b", re.IGNORECASE)
VALUE_TYPE_PROPERTY_RE = re.compile(
    r"\.(?:Count|Length|Value|Ticks|Milliseconds|Seconds|Minutes|Hours|Days|"
    r".*(?:Time|Seconds|Meters|Liters|Laps|Pct|Count|Index|Idx|Position|Lap))\b"
)
VALUE_TYPE_STATIC_RE = re.compile(r"\b(?:Math|DateTime|DateTimeOffset|TimeSpan|Guid)\.")
SKIP_DIR_NAMES = {".git", ".vs", "bin", "obj", "artifacts"}
KNOWN_EXTERNAL_ENUM_MEMBERS = {
    # irsdkSharp 0.9.0. Keep this small and only for third-party enums used by the app.
    "BroadcastMessageTypes": {
        "CamSwitchPos",
        "CamSwitchNum",
        "CamSetState",
        "ReplaySetPlaySpeed",
        "ReplaySetPlayPosition",
        "ReplaySearch",
        "ReplaySetState",
        "ReloadTextures",
        "ChatCommand",
        "PitCommand",
        "TelemCommand",
    },
    "TelemCommandModeTypes": {
        "Stop",
        "Start",
        "Restart",
    },
}
KEYWORDS = {
    "add",
    "class",
    "delegate",
    "enum",
    "event",
    "explicit",
    "get",
    "if",
    "implicit",
    "init",
    "interface",
    "namespace",
    "new",
    "operator",
    "out",
    "params",
    "private",
    "protected",
    "public",
    "readonly",
    "ref",
    "record",
    "remove",
    "required",
    "return",
    "scoped",
    "set",
    "struct",
    "this",
    "where",
}


@dataclass(frozen=True)
class Occurrence:
    name: str
    kind: str
    path: Path
    line: int


@dataclass(frozen=True)
class TypeDeclaration:
    name: str
    kind: str
    path: Path
    line: int
    body_start: int
    body_end: int
    params_start: int | None
    params_end: int | None


@dataclass(frozen=True)
class DuplicateIssue:
    type_decl: TypeDeclaration
    member_name: str
    occurrences: tuple[Occurrence, ...]


@dataclass(frozen=True)
class PrimaryConstructorScopeIssue:
    type_decl: TypeDeclaration
    parameter_name: str
    parameter_line: int
    nested_type_name: str
    nested_type_line: int


@dataclass(frozen=True)
class NullableConditionalInferenceIssue:
    path: Path
    line: int
    variable_name: str
    null_branch: str


@dataclass(frozen=True)
class ExternalEnumMemberIssue:
    path: Path
    line: int
    enum_name: str
    member_name: str
    allowed_members: tuple[str, ...]


def normalize_identifier(identifier: str) -> str:
    return identifier[1:] if identifier.startswith("@") else identifier


def line_starts_for(text: str) -> list[int]:
    starts = [0]
    starts.extend(match.end() for match in re.finditer(r"\n", text))
    return starts


def line_for_offset(line_starts: list[int], offset: int) -> int:
    return bisect.bisect_right(line_starts, offset)


def strip_comments_and_strings(source: str) -> str:
    chars = list(source)
    i = 0
    length = len(source)

    def blank(start: int, end: int) -> None:
        for index in range(start, end):
            if chars[index] != "\n":
                chars[index] = " "

    while i < length:
        if source.startswith("//", i):
            end = source.find("\n", i)
            end = length if end == -1 else end
            blank(i, end)
            i = end
            continue

        if source.startswith("/*", i):
            end = source.find("*/", i + 2)
            end = length if end == -1 else end + 2
            blank(i, end)
            i = end
            continue

        if source.startswith('"""', i):
            quote_count = 3
            while i + quote_count < length and source[i + quote_count] == '"':
                quote_count += 1
            delimiter = '"' * quote_count
            end = source.find(delimiter, i + quote_count)
            end = length if end == -1 else end + quote_count
            blank(i, end)
            i = end
            continue

        if source.startswith('@"', i):
            end = i + 2
            while end < length:
                if source[end] == '"' and end + 1 < length and source[end + 1] == '"':
                    end += 2
                    continue
                if source[end] == '"':
                    end += 1
                    break
                end += 1
            blank(i, end)
            i = end
            continue

        if source[i] == '"':
            end = i + 1
            escaped = False
            while end < length:
                char = source[end]
                if escaped:
                    escaped = False
                elif char == "\\":
                    escaped = True
                elif char == '"':
                    end += 1
                    break
                end += 1
            blank(i, end)
            i = end
            continue

        if source[i] == "'":
            end = i + 1
            escaped = False
            while end < length:
                char = source[end]
                if escaped:
                    escaped = False
                elif char == "\\":
                    escaped = True
                elif char == "'":
                    end += 1
                    break
                end += 1
            blank(i, end)
            i = end
            continue

        i += 1

    return "".join(chars)


def find_matching(text: str, open_index: int, open_char: str, close_char: str) -> int:
    depth = 0
    for index in range(open_index, len(text)):
        char = text[index]
        if char == open_char:
            depth += 1
        elif char == close_char:
            depth -= 1
            if depth == 0:
                return index
    return -1


def skip_whitespace(text: str, index: int) -> int:
    while index < len(text) and text[index].isspace():
        index += 1
    return index


def iter_type_declarations(path: Path, source: str, stripped: str) -> list[TypeDeclaration]:
    line_starts = line_starts_for(source)
    declarations: list[TypeDeclaration] = []

    for match in TYPE_DECL_RE.finditer(stripped):
        kind = match.group("kind").split()[0]
        name = normalize_identifier(match.group("name"))
        cursor = skip_whitespace(stripped, match.end())
        params_start: int | None = None
        params_end: int | None = None

        if kind == "record" and cursor < len(stripped) and stripped[cursor] == "(":
            close_paren = find_matching(stripped, cursor, "(", ")")
            if close_paren != -1:
                params_start = cursor + 1
                params_end = close_paren
                cursor = close_paren + 1

        body_start = -1
        paren_depth = 0
        bracket_depth = 0
        index = cursor
        while index < len(stripped):
            char = stripped[index]
            if char == "(":
                paren_depth += 1
            elif char == ")" and paren_depth > 0:
                paren_depth -= 1
            elif char == "[":
                bracket_depth += 1
            elif char == "]" and bracket_depth > 0:
                bracket_depth -= 1
            elif char == "{" and paren_depth == 0 and bracket_depth == 0:
                body_start = index
                break
            elif char == ";" and paren_depth == 0 and bracket_depth == 0:
                break
            index += 1

        if body_start == -1:
            continue

        body_end = find_matching(stripped, body_start, "{", "}")
        if body_end == -1:
            continue

        declarations.append(
            TypeDeclaration(
                name=name,
                kind=kind,
                path=path,
                line=line_for_offset(line_starts, match.start()),
                body_start=body_start,
                body_end=body_end,
                params_start=params_start,
                params_end=params_end,
            )
        )

    return declarations


def split_top_level_commas(text: str) -> list[tuple[str, int]]:
    parts: list[tuple[str, int]] = []
    start = 0
    paren_depth = 0
    bracket_depth = 0
    brace_depth = 0
    angle_depth = 0

    for index, char in enumerate(text):
        if char == "(":
            paren_depth += 1
        elif char == ")" and paren_depth > 0:
            paren_depth -= 1
        elif char == "[":
            bracket_depth += 1
        elif char == "]" and bracket_depth > 0:
            bracket_depth -= 1
        elif char == "{":
            brace_depth += 1
        elif char == "}" and brace_depth > 0:
            brace_depth -= 1
        elif char == "<":
            angle_depth += 1
        elif char == ">" and angle_depth > 0:
            angle_depth -= 1
        elif (
            char == ","
            and paren_depth == 0
            and bracket_depth == 0
            and brace_depth == 0
            and angle_depth == 0
        ):
            parts.append((text[start:index], start))
            start = index + 1

    parts.append((text[start:], start))
    return parts


def find_top_level_sequence(text: str, sequence: str) -> int:
    paren_depth = 0
    bracket_depth = 0
    brace_depth = 0
    angle_depth = 0
    index = 0
    while index < len(text):
        char = text[index]
        if (
            text.startswith(sequence, index)
            and paren_depth == 0
            and bracket_depth == 0
            and brace_depth == 0
            and angle_depth == 0
        ):
            return index
        if char == "(":
            paren_depth += 1
        elif char == ")" and paren_depth > 0:
            paren_depth -= 1
        elif char == "[":
            bracket_depth += 1
        elif char == "]" and bracket_depth > 0:
            bracket_depth -= 1
        elif char == "{":
            brace_depth += 1
        elif char == "}" and brace_depth > 0:
            brace_depth -= 1
        elif char == "<":
            angle_depth += 1
        elif char == ">" and angle_depth > 0:
            angle_depth -= 1
        index += 1
    return -1


def find_statement_end(text: str, start: int) -> int:
    paren_depth = 0
    bracket_depth = 0
    brace_depth = 0

    for index in range(start, len(text)):
        char = text[index]
        if char == "(":
            paren_depth += 1
        elif char == ")" and paren_depth > 0:
            paren_depth -= 1
        elif char == "[":
            bracket_depth += 1
        elif char == "]" and bracket_depth > 0:
            bracket_depth -= 1
        elif char == "{":
            brace_depth += 1
        elif char == "}" and brace_depth > 0:
            brace_depth -= 1
        elif char == ";" and paren_depth == 0 and bracket_depth == 0 and brace_depth == 0:
            return index

    return -1


def is_top_level_conditional_question(text: str, index: int) -> bool:
    next_char = text[index + 1] if index + 1 < len(text) else ""
    previous_char = text[index - 1] if index > 0 else ""
    if next_char in {".", "?", "["}:
        return False
    if previous_char == "?":
        return False
    return True


def find_top_level_ternary_parts(text: str) -> tuple[str, str, str] | None:
    paren_depth = 0
    bracket_depth = 0
    brace_depth = 0
    question_index = -1

    for index, char in enumerate(text):
        if char == "(":
            paren_depth += 1
        elif char == ")" and paren_depth > 0:
            paren_depth -= 1
        elif char == "[":
            bracket_depth += 1
        elif char == "]" and bracket_depth > 0:
            bracket_depth -= 1
        elif char == "{":
            brace_depth += 1
        elif char == "}" and brace_depth > 0:
            brace_depth -= 1
        elif (
            char == "?"
            and paren_depth == 0
            and bracket_depth == 0
            and brace_depth == 0
            and question_index == -1
            and is_top_level_conditional_question(text, index)
        ):
            question_index = index
        elif (
            char == ":"
            and question_index != -1
            and paren_depth == 0
            and bracket_depth == 0
            and brace_depth == 0
        ):
            return text[:question_index], text[question_index + 1 : index], text[index + 1 :]

    return None


def is_plain_null_branch(text: str) -> bool:
    return text.strip() == "null"


def has_method_call(text: str) -> bool:
    return re.search(r"\b[A-Za-z_][A-Za-z0-9_]*\s*\(", text) is not None


def looks_like_value_type_expression(text: str) -> bool:
    expression = text.strip()
    if not expression or NULLABLE_CAST_RE.match(expression):
        return False
    if '"' in expression:
        return False
    if re.search(r"[*/%]", expression):
        return True
    if NUMERIC_LITERAL_RE.search(expression):
        return True
    if expression in {"true", "false"}:
        return True
    if VALUE_TYPE_STATIC_RE.search(expression):
        return True
    if not has_method_call(expression) and VALUE_TYPE_PROPERTY_RE.search(expression):
        return True
    return False


def last_identifier(text: str) -> tuple[str | None, int]:
    matches = list(IDENT_RE.finditer(text))
    if not matches:
        return None, -1
    match = matches[-1]
    return normalize_identifier(match.group(0)), match.start()


def remove_leading_attributes(header: str) -> str:
    previous = None
    result = header.lstrip()
    while previous != result:
        previous = result
        result = re.sub(r"^\[[^\]]*\]\s*", "", result, flags=re.DOTALL).lstrip()
    return result


def extract_record_parameter_occurrences(
    type_decl: TypeDeclaration,
    source: str,
    stripped: str,
) -> list[Occurrence]:
    if type_decl.params_start is None or type_decl.params_end is None:
        return []

    line_starts = line_starts_for(source)
    params_text = stripped[type_decl.params_start : type_decl.params_end]
    occurrences: list[Occurrence] = []

    for segment, segment_start in split_top_level_commas(params_text):
        equals = find_top_level_sequence(segment, "=")
        clean_segment = segment if equals == -1 else segment[:equals]
        name, relative_index = last_identifier(clean_segment)
        if name is None or name in KEYWORDS:
            continue
        absolute_index = type_decl.params_start + segment_start + relative_index
        occurrences.append(
            Occurrence(
                name=name,
                kind="record parameter property",
                path=type_decl.path,
                line=line_for_offset(line_starts, absolute_index),
            )
        )

    return occurrences


def extract_record_parameter_type_references(
    type_decl: TypeDeclaration,
    source: str,
    stripped: str,
) -> list[tuple[str, str, int]]:
    if type_decl.params_start is None or type_decl.params_end is None:
        return []

    line_starts = line_starts_for(source)
    params_text = stripped[type_decl.params_start : type_decl.params_end]
    references: list[tuple[str, str, int]] = []

    for segment, segment_start in split_top_level_commas(params_text):
        equals = find_top_level_sequence(segment, "=")
        clean_segment = segment if equals == -1 else segment[:equals]
        clean_segment = remove_leading_attributes(clean_segment)
        matches = list(IDENT_RE.finditer(clean_segment))
        if len(matches) < 2:
            continue

        parameter_match = matches[-1]
        parameter_name = normalize_identifier(parameter_match.group(0))
        type_text = clean_segment[: parameter_match.start()]
        absolute_parameter_index = type_decl.params_start + segment_start + parameter_match.start()
        parameter_line = line_for_offset(line_starts, absolute_parameter_index)

        for type_match in IDENT_RE.finditer(type_text):
            type_name = normalize_identifier(type_match.group(0))
            if type_name in KEYWORDS:
                continue
            references.append((parameter_name, type_name, parameter_line))

    return references


def extract_member_from_header(
    type_decl: TypeDeclaration,
    source: str,
    header: str,
    header_start: int,
    terminator: str,
) -> list[Occurrence]:
    if not header.strip():
        return []

    stripped_header = remove_leading_attributes(header)
    if not stripped_header or stripped_header.lstrip().startswith(("=", ",", ":")):
        return []

    arrow_index = find_top_level_sequence(stripped_header, "=>")
    has_expression_body = arrow_index != -1
    if has_expression_body:
        stripped_header = stripped_header[:arrow_index]

    equals_index = find_top_level_sequence(stripped_header, "=")
    if equals_index != -1:
        stripped_header = stripped_header[:equals_index]

    if re.search(r"\b(?:operator|implicit|explicit)\b", stripped_header):
        return []

    line_starts = line_starts_for(source)
    type_match = re.search(
        r"\b(?:class|struct|interface|enum|record(?:\s+(?:class|struct))?)\s+"
        r"(@?[A-Za-z_][A-Za-z0-9_]*)",
        stripped_header,
    )
    if type_match is not None:
        name = normalize_identifier(type_match.group(1))
        return [
            Occurrence(
                name=name,
                kind="nested type",
                path=type_decl.path,
                line=line_for_offset(line_starts, header_start + header.rfind(type_match.group(1))),
            )
        ]

    paren_index = find_top_level_sequence(stripped_header, "(")
    if paren_index != -1:
        name, relative_index = last_identifier(stripped_header[:paren_index])
        if name is None or name == type_decl.name or name in KEYWORDS:
            return []
        return [
            Occurrence(
                name=name,
                kind="method",
                path=type_decl.path,
                line=line_for_offset(line_starts, header_start + header.rfind(name)),
            )
        ]

    if re.search(r"\bthis\s*\[", stripped_header):
        return []

    occurrences: list[Occurrence] = []
    segments = split_top_level_commas(stripped_header)
    for segment, _ in segments:
        segment_equals = find_top_level_sequence(segment, "=")
        clean_segment = segment if segment_equals == -1 else segment[:segment_equals]
        name, _ = last_identifier(clean_segment)
        if name is None or name in KEYWORDS:
            continue
        if re.search(r"\bevent\b", clean_segment):
            kind = "event"
        elif terminator == "{" or has_expression_body:
            kind = "property"
        else:
            kind = "field"
        occurrences.append(
            Occurrence(
                name=name,
                kind=kind,
                path=type_decl.path,
                line=line_for_offset(line_starts, header_start + header.rfind(name)),
            )
        )

    return occurrences


def extract_body_member_occurrences(
    type_decl: TypeDeclaration,
    source: str,
    stripped: str,
) -> list[Occurrence]:
    occurrences: list[Occurrence] = []
    body = stripped[type_decl.body_start + 1 : type_decl.body_end]
    body_offset = type_decl.body_start + 1
    segment_start = 0
    paren_depth = 0
    bracket_depth = 0
    index = 0

    while index < len(body):
        char = body[index]
        if char == "(":
            paren_depth += 1
        elif char == ")" and paren_depth > 0:
            paren_depth -= 1
        elif char == "[":
            bracket_depth += 1
        elif char == "]" and bracket_depth > 0:
            bracket_depth -= 1
        elif char == "{" and paren_depth == 0 and bracket_depth == 0:
            header = body[segment_start:index]
            occurrences.extend(
                extract_member_from_header(
                    type_decl,
                    source,
                    header,
                    body_offset + segment_start,
                    "{",
                )
            )
            global_open = body_offset + index
            global_close = find_matching(stripped, global_open, "{", "}")
            if global_close == -1:
                break
            index = global_close - body_offset + 1
            segment_start = index
            continue
        elif char == ";" and paren_depth == 0 and bracket_depth == 0:
            header = body[segment_start:index]
            occurrences.extend(
                extract_member_from_header(
                    type_decl,
                    source,
                    header,
                    body_offset + segment_start,
                    ";",
                )
            )
            segment_start = index + 1
        index += 1

    return occurrences


def find_nullable_conditional_inference_issues(path: Path, source: str, stripped: str) -> list[NullableConditionalInferenceIssue]:
    line_starts = line_starts_for(source)
    issues: list[NullableConditionalInferenceIssue] = []

    for match in VAR_DECL_RE.finditer(stripped):
        statement_end = find_statement_end(stripped, match.end())
        if statement_end == -1:
            continue

        expression = stripped[match.end() : statement_end]
        ternary = find_top_level_ternary_parts(expression)
        if ternary is None:
            continue

        _, true_branch, false_branch = ternary
        if is_plain_null_branch(true_branch):
            null_branch = "true"
            non_null_branch = false_branch
        elif is_plain_null_branch(false_branch):
            null_branch = "false"
            non_null_branch = true_branch
        else:
            continue

        if not looks_like_value_type_expression(non_null_branch):
            continue

        issues.append(
            NullableConditionalInferenceIssue(
                path=path,
                line=line_for_offset(line_starts, match.start()),
                variable_name=normalize_identifier(match.group("name")),
                null_branch=null_branch,
            )
        )

    return issues


def find_external_enum_member_issues(path: Path, source: str, stripped: str) -> list[ExternalEnumMemberIssue]:
    line_starts = line_starts_for(source)
    issues: list[ExternalEnumMemberIssue] = []

    for match in MEMBER_ACCESS_RE.finditer(stripped):
        enum_name = match.group("type")
        allowed_members = KNOWN_EXTERNAL_ENUM_MEMBERS.get(enum_name)
        if allowed_members is None:
            continue

        member_name = match.group("member")
        if member_name in allowed_members:
            continue

        issues.append(
            ExternalEnumMemberIssue(
                path=path,
                line=line_for_offset(line_starts, match.start("member")),
                enum_name=enum_name,
                member_name=member_name,
                allowed_members=tuple(sorted(allowed_members)),
            )
        )

    return issues


def is_invalid_duplicate(occurrences: list[Occurrence]) -> bool:
    if len(occurrences) <= 1:
        return False
    kinds = {occurrence.kind for occurrence in occurrences}
    return kinds != {"method"}


def analyze_text(
    path: Path,
    source: str,
) -> tuple[
    list[DuplicateIssue],
    list[PrimaryConstructorScopeIssue],
    list[NullableConditionalInferenceIssue],
    list[ExternalEnumMemberIssue],
]:
    stripped = strip_comments_and_strings(source)
    duplicate_issues: list[DuplicateIssue] = []
    scope_issues: list[PrimaryConstructorScopeIssue] = []
    nullable_conditional_issues = find_nullable_conditional_inference_issues(path, source, stripped)
    external_enum_issues = find_external_enum_member_issues(path, source, stripped)

    for type_decl in iter_type_declarations(path, source, stripped):
        occurrences = extract_record_parameter_occurrences(type_decl, source, stripped)
        occurrences.extend(extract_body_member_occurrences(type_decl, source, stripped))

        by_name: dict[str, list[Occurrence]] = {}
        for occurrence in occurrences:
            by_name.setdefault(occurrence.name, []).append(occurrence)

        for member_name, duplicate_occurrences in sorted(by_name.items()):
            if is_invalid_duplicate(duplicate_occurrences):
                duplicate_issues.append(
                    DuplicateIssue(
                        type_decl=type_decl,
                        member_name=member_name,
                        occurrences=tuple(duplicate_occurrences),
                    )
                )

        nested_type_lines = {
            occurrence.name: occurrence.line
            for occurrence in occurrences
            if occurrence.kind == "nested type"
        }
        if not nested_type_lines:
            continue

        for parameter_name, type_name, parameter_line in extract_record_parameter_type_references(type_decl, source, stripped):
            if type_name not in nested_type_lines:
                continue
            scope_issues.append(
                PrimaryConstructorScopeIssue(
                    type_decl=type_decl,
                    parameter_name=parameter_name,
                    parameter_line=parameter_line,
                    nested_type_name=type_name,
                    nested_type_line=nested_type_lines[type_name],
                )
            )

    return duplicate_issues, scope_issues, nullable_conditional_issues, external_enum_issues


def analyze_file(
    path: Path,
) -> tuple[
    list[DuplicateIssue],
    list[PrimaryConstructorScopeIssue],
    list[NullableConditionalInferenceIssue],
    list[ExternalEnumMemberIssue],
]:
    try:
        source = path.read_text(encoding="utf-8-sig")
    except UnicodeDecodeError:
        source = path.read_text()
    return analyze_text(path, source)


def collect_csharp_files(paths: list[Path]) -> list[Path]:
    files: list[Path] = []
    for path in paths:
        if path.is_file() and path.suffix == ".cs":
            files.append(path)
            continue
        if not path.is_dir():
            continue
        for candidate in path.rglob("*.cs"):
            if any(part in SKIP_DIR_NAMES for part in candidate.parts):
                continue
            files.append(candidate)
    return sorted(files)


def default_roots() -> list[Path]:
    roots = [Path("src"), Path("tests"), Path("tools")]
    return [root for root in roots if root.exists()] or [Path(".")]


def run_self_test() -> int:
    duplicate_sample = """
internal sealed record LiveWeatherSnapshot(string? SkiesLabel)
{
    private static string? SkiesLabel(int? skies) => null;
    private static string? Overloaded(int value) => null;
    private static string? Overloaded(string value) => null;
}
"""
    clean_sample = """
internal sealed record LiveWeatherSnapshot(string? SkiesLabel)
{
    private static string? FormatSkiesLabel(int? skies) => null;
    private static string? Overloaded(int value) => null;
    private static string? Overloaded(string value) => null;
}
"""
    nested_type_scope_sample = """
internal sealed record CaptureHealth(ActivityBadge Activity)
{
    private sealed record ActivityBadge(string Text);
}
"""
    nullable_conditional_sample = """
internal static class LiveModelBuilder
{
    private static void Build(double? value)
    {
        var meters = value is { } km ? km * 1000d : null;
        var validMeters = value is { } km2 ? km2 * 1000d : (double?)null;
        double? typedMeters = value is { } km3 ? km3 * 1000d : null;
        var label = value is null ? null : "ready";
        var model = value is null ? null : BuildModel();
    }

    private static object BuildModel() => new();
}
"""
    external_enum_sample = """
internal static class IbtLogging
{
    private static void Start()
    {
        _ = TelemCommandModeTypes.ToStart;
        _ = TelemCommandModeTypes.Start;
        _ = BroadcastMessageTypes.TelemCommand;
    }
}
"""
    duplicate_issues, duplicate_scope_issues, duplicate_nullable_issues, duplicate_enum_issues = analyze_text(
        Path("<self-test-duplicate>"),
        duplicate_sample,
    )
    clean_issues, clean_scope_issues, clean_nullable_issues, clean_enum_issues = analyze_text(
        Path("<self-test-clean>"),
        clean_sample,
    )
    nested_duplicate_issues, nested_scope_issues, nested_nullable_issues, nested_enum_issues = analyze_text(
        Path("<self-test-nested-type-scope>"),
        nested_type_scope_sample,
    )
    nullable_duplicate_issues, nullable_scope_issues, nullable_issues, nullable_enum_issues = analyze_text(
        Path("<self-test-nullable-conditional>"),
        nullable_conditional_sample,
    )
    enum_duplicate_issues, enum_scope_issues, enum_nullable_issues, enum_issues = analyze_text(
        Path("<self-test-external-enum>"),
        external_enum_sample,
    )
    if len(duplicate_issues) != 1 or duplicate_issues[0].member_name != "SkiesLabel":
        print("self-test failed: duplicate record property/helper method was not detected", file=sys.stderr)
        return 1
    if duplicate_scope_issues or duplicate_nullable_issues or duplicate_enum_issues:
        print("self-test failed: duplicate member sample reported unrelated issues", file=sys.stderr)
        return 1
    if clean_issues or clean_scope_issues or clean_nullable_issues or clean_enum_issues:
        print("self-test failed: clean sample was reported as an issue", file=sys.stderr)
        return 1
    if nested_duplicate_issues or nested_nullable_issues or nested_enum_issues:
        print("self-test failed: nested type scope sample reported unrelated issues", file=sys.stderr)
        return 1
    if len(nested_scope_issues) != 1 or nested_scope_issues[0].nested_type_name != "ActivityBadge":
        print("self-test failed: primary-constructor nested type reference was not detected", file=sys.stderr)
        return 1
    if nullable_duplicate_issues or nullable_scope_issues or nullable_enum_issues:
        print("self-test failed: nullable conditional sample reported unrelated issues", file=sys.stderr)
        return 1
    if len(nullable_issues) != 1 or nullable_issues[0].variable_name != "meters":
        print("self-test failed: nullable conditional var/null hazard was not detected", file=sys.stderr)
        return 1
    if enum_duplicate_issues or enum_scope_issues or enum_nullable_issues:
        print("self-test failed: external enum sample reported unrelated issues", file=sys.stderr)
        return 1
    if len(enum_issues) != 1 or enum_issues[0].member_name != "ToStart":
        print("self-test failed: external enum member hazard was not detected", file=sys.stderr)
        return 1
    print("self-test passed")
    return 0


def parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Scan C# files for local compile hazards inside type declarations."
    )
    parser.add_argument(
        "paths",
        nargs="*",
        type=Path,
        help="Files or directories to scan. Defaults to src/, tests/, and tools/.",
    )
    parser.add_argument(
        "--self-test",
        action="store_true",
        help="Run a small in-memory validation of the duplicate detector.",
    )
    return parser.parse_args(argv)


def main(argv: list[str]) -> int:
    args = parse_args(argv)
    if args.self_test:
        return run_self_test()

    paths = args.paths or default_roots()
    files = collect_csharp_files(paths)
    if not files:
        print("No C# files found.")
        return 0

    all_duplicate_issues: list[DuplicateIssue] = []
    all_scope_issues: list[PrimaryConstructorScopeIssue] = []
    all_nullable_conditional_issues: list[NullableConditionalInferenceIssue] = []
    all_external_enum_issues: list[ExternalEnumMemberIssue] = []
    for path in files:
        duplicate_issues, scope_issues, nullable_conditional_issues, external_enum_issues = analyze_file(path)
        all_duplicate_issues.extend(duplicate_issues)
        all_scope_issues.extend(scope_issues)
        all_nullable_conditional_issues.extend(nullable_conditional_issues)
        all_external_enum_issues.extend(external_enum_issues)

    if (
        not all_duplicate_issues
        and not all_scope_issues
        and not all_nullable_conditional_issues
        and not all_external_enum_issues
    ):
        print(f"No local C# compile-shape hazards found in {len(files)} files.")
        return 0

    if all_duplicate_issues:
        print("Duplicate C# member names found:")
        for issue in all_duplicate_issues:
            type_decl = issue.type_decl
            print(f"{type_decl.path}:{type_decl.line}: type '{type_decl.name}' duplicates '{issue.member_name}'")
            for occurrence in issue.occurrences:
                print(f"  {occurrence.path}:{occurrence.line}: {occurrence.kind}")

    if all_scope_issues:
        print("Primary-constructor nested-type scope hazards found:")
        for issue in all_scope_issues:
            type_decl = issue.type_decl
            print(
                f"{type_decl.path}:{type_decl.line}: type '{type_decl.name}' primary constructor "
                f"parameter '{issue.parameter_name}' uses nested type '{issue.nested_type_name}'"
            )
            print(f"  {type_decl.path}:{issue.parameter_line}: parameter")
            print(f"  {type_decl.path}:{issue.nested_type_line}: nested type declaration")

    if all_nullable_conditional_issues:
        print("Targetless nullable conditional inference hazards found:")
        for issue in all_nullable_conditional_issues:
            print(
                f"{issue.path}:{issue.line}: var '{issue.variable_name}' uses a conditional expression "
                f"with a plain null {issue.null_branch} branch"
            )
            print("  Use an explicit nullable local type or cast the null branch, e.g. ': (double?)null'.")

    if all_external_enum_issues:
        print("Known external enum member hazards found:")
        for issue in all_external_enum_issues:
            allowed = ", ".join(issue.allowed_members)
            print(
                f"{issue.path}:{issue.line}: '{issue.enum_name}' does not list member "
                f"'{issue.member_name}' in the local validation contract"
            )
            print(f"  Known members: {allowed}")

    return 1


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
