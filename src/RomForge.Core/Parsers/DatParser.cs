using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using RomForge.Core.Models;

namespace RomForge.Core.Parsers
{
    internal static class DatParser
    {
        /// <exception cref="InvalidDataException">The stream does not contain a valid DAT XML document.</exception>
        public static async Task<DatFile> ParseAsync(
            Stream stream,
            CancellationToken cancellationToken = default
        )
        {
            XDocument doc = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);
            XElement root =
                doc.Root ?? throw new InvalidDataException("DAT file has no root element.");

            return new DatFile
            {
                Header = ParseConfiguration(root.ElementCI(Xml.Configuration)),
                Games =
                    root.ElementCI(nameof(DatFile.Games))
                        ?.ElementsCI(nameof(Game))
                        .Select(ParseGame)
                        .ToList()
                    ?? [],
            };
        }

        private static DatHeader ParseConfiguration(XElement? config)
        {
            if (config is null)
                return new DatHeader();

            XElement? newDat = config.ElementCI(Xml.NewDat);
            XElement? datUrl = newDat?.ElementCI(Xml.DatURL);

            return new DatHeader
            {
                DatName = (string?)config.ElementCI(nameof(DatHeader.DatName)) ?? string.Empty,
                System = (string?)config.ElementCI(nameof(DatHeader.System)) ?? string.Empty,
                DatVersion = ParseInt(config.ElementCI(nameof(DatHeader.DatVersion))),
                ImFolder = NullIfEmpty((string?)config.ElementCI(nameof(DatHeader.ImFolder))),
                ScreenshotsWidth = ParseInt(config.ElementCI(nameof(DatHeader.ScreenshotsWidth))),
                ScreenshotsHeight = ParseInt(config.ElementCI(nameof(DatHeader.ScreenshotsHeight))),
                NewDatVersionUrl = NullIfEmpty((string?)newDat?.ElementCI(Xml.DatVersionURL)),
                NewDatUrl = NullIfEmpty((string?)datUrl),
                NewDatFileName = NullIfEmpty((string?)datUrl?.Attribute(Xml.FileName)),
                NewImUrl = NullIfEmpty((string?)newDat?.ElementCI(Xml.ImURL)),
            };
        }

        private static Game ParseGame(XElement game)
        {
            XElement? files = game.ElementCI(nameof(Game.Files));
            XElement? romCrc = files?.ElementCI(nameof(GameFiles.RomCrc));

            return new Game
            {
                ImageNumber = ParseInt(game.ElementCI(nameof(Game.ImageNumber))),
                ReleaseNumber = ParseInt(game.ElementCI(nameof(Game.ReleaseNumber))),
                Title = (string?)game.ElementCI(nameof(Game.Title)) ?? string.Empty,
                SaveType = NullIfEmpty((string?)game.ElementCI(nameof(Game.SaveType))),
                RomSize = ParseLong(game.ElementCI(nameof(Game.RomSize))),
                Publisher = NullIfEmpty((string?)game.ElementCI(nameof(Game.Publisher))),
                Location = ParseInt(game.ElementCI(nameof(Game.Location))),
                Language = ParseInt(game.ElementCI(nameof(Game.Language))),
                SourceRom = NullIfEmpty((string?)game.ElementCI(nameof(Game.SourceRom))),
                Comment = NullIfEmpty((string?)game.ElementCI(nameof(Game.Comment))),
                DuplicateId = ParseInt(game.ElementCI(nameof(Game.DuplicateId))),
                Files = new GameFiles
                {
                    RomCrc = ParseHexUInt32(romCrc),
                    RomExtension = (string?)romCrc?.Attribute(Xml.Extension) ?? string.Empty,
                },
                Im1Crc = ParseHexUInt32Nullable(game.ElementCI(nameof(Game.Im1Crc))),
                Im2Crc = ParseHexUInt32Nullable(game.ElementCI(nameof(Game.Im2Crc))),
            };
        }

        private static int ParseInt(XElement? element) =>
            int.TryParse((string?)element, out int value) ? value : 0;

        private static long ParseLong(XElement? element) =>
            long.TryParse((string?)element, out long value) ? value : 0;

        private static uint ParseHexUInt32(XElement? element)
        {
            string? text = (string?)element;
            return string.IsNullOrEmpty(text) ? 0u : Convert.ToUInt32(text, 16);
        }

        private static uint? ParseHexUInt32Nullable(XElement? element)
        {
            string? text = (string?)element;
            return string.IsNullOrEmpty(text) ? null : Convert.ToUInt32(text, 16);
        }

        private static string? NullIfEmpty(string? value) =>
            string.IsNullOrEmpty(value) ? null : value;

        // XML names with no direct C# property equivalent — all other names come from nameof() on model properties.
        private static class Xml
        {
            public const string Configuration = "configuration";
            public const string NewDat = "newDat";
            public const string DatVersionURL = "datVersionURL";
            public const string DatURL = "datURL";
            public const string ImURL = "imURL";
            public const string Extension = "extension";
            public const string FileName = "fileName";
        }
    }
}
