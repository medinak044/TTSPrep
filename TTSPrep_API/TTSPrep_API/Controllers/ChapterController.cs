﻿using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using TTSPrep_API.DTOs;
using TTSPrep_API.Helpers;
using TTSPrep_API.Models;
using TTSPrep_API.Repository.IRepository;

namespace TTSPrep_API.Controllers;

[Route("api/[controller]")]
[ApiController]
public class ChapterController : ControllerBase
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly UserManager<AppUser> _userManager;

    public ChapterController(
        IUnitOfWork unitOfWork,
        UserManager<AppUser> userManager
        )
    {
        _unitOfWork = unitOfWork;
        _userManager = userManager;
    }

    [HttpGet("GetAllChapters")]
    public async Task<ActionResult> GetAllChapters()
    {
        var result = await _unitOfWork.Chapters.GetAllAsync();
        return Ok(result);
    }

    [HttpGet("GetChaptersByProjectId/{projectId}")]
    public async Task<ActionResult> GetChaptersByProjectId(string projectId)
    {
        var chapters = _unitOfWork.Chapters.GetSome(c => c.ProjectId == projectId).ToList();
        return Ok(chapters);
    }

    [HttpGet("GetChapterById/{chapterId}")]
    public async Task<ActionResult> GetChapterById(string chapterId)
    {
        var chapter = await _unitOfWork.Chapters.GetByIdAsync(chapterId);
        return Ok(chapter);
    }

    [HttpPost("CreateChapter")]
    public async Task<ActionResult> CreateChapter(Chapter chapterForm)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        // In case ModelState doesn't throw the error
        if (string.IsNullOrEmpty(chapterForm.ProjectId))
        {
            return BadRequest(new AuthResult()
            {
                Success = false,
                Messages = new List<string>() { "Unable to get project Id" }
            });
        }

        // Get a list of all chapters of the project
        var chapters = _unitOfWork.Chapters.GetSome(c => c.ProjectId == chapterForm.ProjectId).ToList();
        // The new chapter must have an order number increment the highest number by 1
        int orderNumber = 0;
        foreach (var c in chapters)
        {
            if (c.OrderNumber > orderNumber)
                orderNumber = c.OrderNumber;
        }
        orderNumber += 1; // Order number must at least start at 1

        string newId = Guid.NewGuid().ToString();

        // Map values
        var chapter = new Chapter
        {
            Id = newId,
            Title = chapterForm.Title.IsNullOrEmpty() ? $"Chapter {orderNumber}" : chapterForm.Title,
            OrderNumber = orderNumber,
            ProjectId = chapterForm.ProjectId,
        };

        await _unitOfWork.Chapters.AddAsync(chapter);
        if (!await _unitOfWork.SaveAsync())
        {
            return BadRequest(new AuthResult()
            {
                Success = false,
                Messages = new List<string>() { "Something went wrong while saving" }
            });
        }
        else
        {
            #region Add a default TextBlockLabel
            var textBlockLabel = new TextBlockLabel
            {
                Id = Guid.NewGuid().ToString(),
                Name = "Narration",
                ChapterId = newId
            };

            await _unitOfWork.TextBlockLabels.AddAsync(textBlockLabel);
            if (!await _unitOfWork.SaveAsync())
            {
                return BadRequest(new AuthResult()
                {
                    Success = false,
                    Messages = new List<string>() { "Something went wrong while saving" }
                });
            }
            #endregion
        }

        return Ok(chapter);
    }

    // Call this method first from client before deleting a chapter
    [HttpPatch("UpdateChapterOrderNumber")]
    public async Task<ActionResult> UpdateChapterOrderNumber([FromBody] Chapter chapterForm)
    {
        if (chapterForm.ProjectId.IsNullOrEmpty())
        {
            return BadRequest(new AuthResult()
            {
                Success = false,
                Messages = new List<string>() { "Missing project id" }
            });
        }

        #region Update order numbers of chapters
        // Get all the chapters within the same project as the deleted chapter
        var allProjectChapters = _unitOfWork.Chapters.GetSome(c => c.ProjectId == chapterForm.ProjectId)
            .OrderBy(c => c.OrderNumber).ToList();
        var updatedChapter = allProjectChapters.FirstOrDefault(c => c.Id == chapterForm.Id);
        var filteredChapters = allProjectChapters;

        // Relocating to the first chapter shifts up chapters
        if (chapterForm.OrderNumber == 1)
        {
            filteredChapters = allProjectChapters.Where(c =>
            (c.ProjectId == chapterForm.ProjectId)
            && (c.OrderNumber < updatedChapter.OrderNumber) 
            ).OrderBy(c => c.OrderNumber).ToList();

            foreach (var c in filteredChapters)
            {
                c.OrderNumber += 1;
                c.Title = c.Title.Equals($"Chapter {c.OrderNumber}") ? c.Title : $"Chapter {c.OrderNumber}";
            }

            _unitOfWork.Chapters.UpdateRange(filteredChapters);
        }
        // Relocating to the last chapter shifts down chapters
        else if (chapterForm.OrderNumber == allProjectChapters.Last().OrderNumber)
        {
            filteredChapters = allProjectChapters.Where(c =>
            (c.ProjectId == chapterForm.ProjectId)
            && (c.OrderNumber > updatedChapter.OrderNumber)
            && (c.OrderNumber <= chapterForm.OrderNumber)
            ).OrderBy(c => c.OrderNumber).ToList();

            foreach (var c in filteredChapters)
            {
                c.OrderNumber -= 1;
                c.Title = c.Title.Equals($"Chapter {c.OrderNumber}") ? c.Title : $"Chapter {c.OrderNumber}";
            }
            _unitOfWork.Chapters.UpdateRange(filteredChapters);
        }
        // Relocating to a chapter between first and last chapter shifts other chapters up and down
        else if (chapterForm.OrderNumber < updatedChapter.OrderNumber)
        {
            // Filter in chapters at or above destination order number but not above original order number
            filteredChapters = allProjectChapters.Where(c =>
            (c.ProjectId == chapterForm.ProjectId)
            && (c.OrderNumber >= chapterForm.OrderNumber) 
            && (c.OrderNumber < updatedChapter.OrderNumber)
            ).OrderBy(c => c.OrderNumber).ToList();

            foreach (var c in filteredChapters)
            {
                c.OrderNumber += 1; // Increment order number to in response to making space for the chapter to be updated
                c.Title = c.Title.Equals($"Chapter {c.OrderNumber}") ? c.Title : $"Chapter {c.OrderNumber}";
            }
            _unitOfWork.Chapters.UpdateRange(filteredChapters);
        }
        // Relocating to a chapter between first and last chapter shifts other chapters up and down
        else if (chapterForm.OrderNumber > updatedChapter.OrderNumber)
        {
            // Filter in chapters at or above destination order number but not above original order number
            filteredChapters = allProjectChapters.Where(c =>
            (c.ProjectId == chapterForm.ProjectId)
            && (c.OrderNumber <= chapterForm.OrderNumber)
            && (c.OrderNumber > updatedChapter.OrderNumber)
            ).OrderBy(c => c.OrderNumber).ToList();

            foreach (var c in filteredChapters)
            {
                c.OrderNumber -= 1; // Increment order number to in response to making space for the chapter to be updated
                c.Title = c.Title.Equals($"Chapter {c.OrderNumber}") ? c.Title : $"Chapter {c.OrderNumber}";
            }
            _unitOfWork.Chapters.UpdateRange(filteredChapters);
        }
        #endregion

        // Update the chapter's order number
        updatedChapter.OrderNumber = chapterForm.OrderNumber;
        updatedChapter.Title = updatedChapter.Title.Equals($"Chapter {updatedChapter.OrderNumber}") 
            ? updatedChapter.Title : $"Chapter {updatedChapter.OrderNumber}";

        await _unitOfWork.Chapters.UpdateAsync(updatedChapter);
        if (!await _unitOfWork.SaveAsync())
        {
            return BadRequest(new AuthResult()
            {
                Success = false,
                Messages = new List<string>() { "Something went wrong while saving" }
            });
        }

        return Ok(updatedChapter);
    }


    [HttpPut("UpdateChapter")]
    public async Task<ActionResult> UpdateChapter([FromBody] Chapter chapterForm)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        // Check if exists in db
        var chapter = await _unitOfWork.Chapters.GetByIdAsync(chapterForm.Id);
        if (chapter == null)
        {
            return NotFound(new AuthResult()
            {
                Success = false,
                Messages = new List<string>() { "No matching Id" }
            });
        }

        // Overwrite values
        chapter.Title = chapterForm.Title;

        await _unitOfWork.Chapters.UpdateAsync(chapter);
        if (!await _unitOfWork.SaveAsync())
        {
            return BadRequest(new AuthResult()
            {
                Success = false,
                Messages = new List<string>() { "Something went wrong while saving" }
            });
        }

        return Ok(chapter);
    }

    [HttpDelete("RemoveChapter/{chapterId}")]
    public async Task<ActionResult> RemoveChapter(string chapterId)
    {
        var chapterToBeDeleted = await _unitOfWork.Chapters.GetByIdAsync(chapterId);

        if (chapterToBeDeleted == null)
        {
            return NotFound(new AuthResult()
            {
                Success = false,
                Messages = new List<string>() { "No matching Id" }
            });
        }

        #region Update order numbers of chapters
        // Get all the chapters within the same project as the deleted chapter
        var allProjectChapters = _unitOfWork.Chapters.GetSome(c => c.ProjectId == chapterToBeDeleted.ProjectId).ToList();
        // Filter the collection to only have chapters with order numbers greater than deleted chapter
        var filteredChapters = allProjectChapters.Where(c =>
        (c.OrderNumber > chapterToBeDeleted.OrderNumber) && (c.ProjectId == chapterToBeDeleted.ProjectId));
        foreach (var c in filteredChapters)
        {
            c.OrderNumber -= 1; // Decrease order number to fill the space opened by the deleted chapter
            c.Title = c.Title.Equals($"Chapter {c.OrderNumber}") ? c.Title : $"Chapter {c.OrderNumber}";
        }

        _unitOfWork.Chapters.UpdateRange(filteredChapters);
        #endregion

        await _unitOfWork.Chapters.RemoveAsync(chapterToBeDeleted);
        if (!await _unitOfWork.SaveAsync())
        {
            return BadRequest(new AuthResult()
            {
                Success = false,
                Messages = new List<string>() { "Something went wrong while saving" }
            });
        }

        return Ok(chapterToBeDeleted);
    }
}
