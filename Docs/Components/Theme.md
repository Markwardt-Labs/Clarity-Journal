# Theme and screens

`ClientApp.axaml`, the views under `Client/Frontend/src/Views` and the view models behind them make up the UI, which follows a reference image: a pastel gradient, soft white cards, white pill buttons and a white bottom tab bar.

## Where the look lives

Every color, gradient, icon shape and style class is defined once in `ClientApp.axaml`; a view names resources such as `SoftFill` or classes such as `card`, `pill` and `segment` and never a color. The values were sampled from the reference: the welcome gradient runs from periwinkle violet at the top to pink at the bottom, and every other screen runs the opposite way, from rose pink to soft violet. Panels (cards, chat bubbles, error bars) are slightly transparent white (`SoftFill`) so the gradient shows through; only the confirmation dialog, which sits over a dark scrim, and the tab bar are solid white, and the user's own chat bubbles stay solid white to stand apart from Claude's. text on cards is a dark indigo, text on the gradient is white, and inactive tab icons are a light lilac.

Style classes follow one idea each: `card` for surfaces (`card dialog` for the solid one), `pill` (white) and `accent` (gradient fill) for primary buttons, `ghost` and `plain` for secondary ones, `segment` for the control that picks a summary kind, `day` with `outside`, `entry`, `today` and `selected` for calendar cells, and `underline`, `editor` and `composer` for the three text box looks.

## Screens

None of the tabs has a title at the top. The tab bar names them Journal, Summary, Ask and Options.

- Connect: the violet gradient, the logo bubble, three underlined fields and a white pill button. A changed server certificate adds a "Trust new certificate" button.
- Journal: one translucent panel that fills the screen down to the tab bar, with the same margin on every side, and never changes size: a long entry scrolls inside its text box. The panel shows either today's entry or the month calendar, never both, and tapping the date centered at its top flips between them. The entry is saved by itself: every five seconds if the text changed, when another day is chosen, when the tab is left and immediately when the app leaves the screen, so text typed in the last few seconds is not lost if the system stops the app. There is no Save button, no Delete button and no saved or unsaved label. A microphone button at the top right of the panel adds what is said to the end of the entry. In the calendar only today and earlier days that have an entry can be selected, the others are dimmed and inert, and days with an entry carry a ring; choosing a day, including the one already shown, returns to the entry. An earlier day's entry is read-only and has no microphone. A failed save keeps the text, shows the error at the bottom of the panel and is tried again at the next tick. The hint in the empty entry uses the muted ink color, since the lighter lilac vanished against the translucent panel.
- Summary: a segmented control for Week, Month, Year and Life and one card per summary with its markdown rendered. The server writes the summaries alone, so there is nothing to press, and a kind that has none yet simply shows nothing. "Life" is the app's name for the lifetime summary; the protocol and the server keep calling it the lifetime.
- Ask: bubbles (the user's on the right in white, Claude's on the left in translucent white with markdown), a round trash button at the top right that opens a confirmation dialog before clearing, and a pill message box with a microphone button and a send button.
- Options: the connected server's address, centered, and Disconnect.

## Icons

Icon shapes are given only a height, never also a width: a shape stretched uniformly inside a larger square is drawn at the square's left edge, so narrow icons such as the microphone and the send arrow would sit off-center in their round buttons. With only a height the shape is measured at its own width and its parent centers it. The UI tests measure the rendered pixels to keep this true.

## Navigation and state

`MainViewModel` shows the connect screen or, once a connection is open, a `SessionViewModel` that owns the bottom tab bar and the three screen view models. Leaving the Journal or Ask tab ends a recording in progress and still adds its text to that tab, so the microphone is never left on in the background. The session forwards each server event to every screen, shows a "Reconnecting" banner while the connection is down, and has every screen read its data again after it returns.

## Markdown

`MarkdownView` renders headings, paragraphs, emphasis, inline code, links as underlined text, quotes, bulleted and numbered lists, fenced code and tables as native controls. Summaries and Claude's chat answers use it.

## Checking the UI

The integration tests render each screen headlessly against a real in-process server and assert the gradient colors; setting the `CLARITY_SCREENSHOTS` environment variable to a folder makes them also save a PNG of every screen, which is how the UI is reviewed without a device.
