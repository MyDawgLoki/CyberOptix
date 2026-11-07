# Design

Frosted violet glass for a dark Windows 11 desktop tool. Source of truth for tokens: `src/CyberOptix/Themes/Theme.xaml`.

## World

Direction chosen by the user from their Seelen UI screenshots: a violet-black window over real Windows 11 acrylic blur, translucent rounded cards, pill switches, one magenta-violet accent. Scene: a gamer at a dark desk adjusting settings between sessions, so dark is forced by use, not by habit.

## Materials

- **Window:** DWM system backdrop (Acrylic) via `Services/WindowEffects.cs`, tinted by `Tint` (#A6130E1F). Falls back to solid #17111F when the backdrop is unsupported.
- **Sidebar:** `SidebarTint` (black at 40%) with a 1px divider.
- **Cards:** `CardBrush` (white at 8%), 1px `StrokeBrush`, 14px radius, 6px inner padding. Rows inside are separated by `Divider`. No nested cards.
- **Depth:** only the ComboBox popup and desktop widgets carry a shadow.

## Color

Restrained: neutrals plus one accent.

| Token | Value | Use |
|---|---|---|
| AccentBrush | #B146C2 | switches on, primary button, slider fill, focus |
| AccentGradient | #C656D6 to #9A3BB5 | selected nav item, brand mark |
| TextBrush | #F4EFF8 | primary text |
| TextDimBrush | #B2A8C0 | descriptions, secondary |
| TextFaintBrush | #9A90AD | hints, footer (about 4.7:1) |
| StrokeBrush / DividerBrush | white at 12% / 8% | hairlines |

## Type and icons

Segoe UI Variable Text (system font), 14px body, 28px page title (SemiBold), 14px SemiBold group titles, 12px descriptions. Icons are Segoe Fluent Icons glyphs, never emoji.

## Components

SettingRow (title, description, control on the right), switch (CheckBox; Content = title, Tag = description), PrimaryButton and secondary Button, Segment (mode toggle), NavItem, themed ComboBox, Slider, TextBox (Tag = hint), ListBox, slim ScrollBar, KeyCap.

## Motion

One authored moment: pages fade in while rising 10px (220 ms, cubic ease-out). Switch thumbs slide 18px (180 ms). No other animation.

## Not yet verified

Light-wallpaper legibility of the acrylic tint, non-default DPI scaling, and high-contrast mode.
