﻿using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TTSPrep_API.Models;

public class TextBlockLabel
{
    [Key]
    public string Id { get; set; }
    public string Name { get; set; }
    [ForeignKey(nameof(Chapter))]
    public string ChapterId { get; set; }
}
