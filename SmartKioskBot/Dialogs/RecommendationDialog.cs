﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Connector;
using SmartKioskBot.Controllers;
using SmartKioskBot.Models;
using SmartKioskBot.UI;
using static SmartKioskBot.Models.Context;
using SmartKioskBot.Logic;
using MongoDB.Driver;
using SmartKioskBot.Helpers;
using MongoDB.Bson;

namespace SmartKioskBot.Dialogs 
{
    [Serializable]
    public class RecommendationDialog : IDialog<object>
    {
        private List<Filter> filtersApplied;
        private User user;
        private ObjectId lastFetchId;

        public RecommendationDialog(User user)
        {
            this.user = user;

            //recommendation type
            this. filtersApplied = new List<Filter>(CRMController.GetMostPopularFilters(user.Id, Constants.MAX_N_FILTERS_RECOMM));

            //Default
            if (filtersApplied == null || filtersApplied.Count == 0)
                this.filtersApplied.Add(FilterLogic.DEFAULT_RECOMMENDATION_FILTER);
        }

        public async Task StartAsync(IDialogContext context)
        {
            await ShowRecommendations(context, null);
        }

        private async Task ShowRecommendations(IDialogContext context, IAwaitable<object> result)
        {
            List<Product> products = new List<Product>();

            while (true)
            {
                FilterDefinition<Product> joinedFilters = FilterLogic.GetJoinedFilter(this.filtersApplied);

                //fetch +1 product to see if pagination is needed
                products = ProductController.getProductsFilter(
                joinedFilters,
                Constants.N_ITEMS_CARROUSSEL + 1,
                this.lastFetchId);

                //filters didn't retrieved any products at the first try
                if (products.Count == 0 && lastFetchId == null)
                    filtersApplied.RemoveAt(filtersApplied.Count - 1);
                else
                    break;
            }
            
            if(products.Count > Constants.N_ITEMS_CARROUSSEL) 
                lastFetchId = products[products.Count - 2].Id;
            
            var reply = context.MakeMessage();
            reply.AttachmentLayout = AttachmentLayoutTypes.Carousel;
            List<Attachment> cards = new List<Attachment>();

            for (int i = 0; i < products.Count && i < Constants.N_ITEMS_CARROUSSEL; i++)
            {
                cards.Add(ProductCard.GetProductCard(products[i], ProductCard.CardType.RECOMMENDATION).ToAttachment());
            }

            reply.Attachments = cards;
            
            await context.PostAsync(reply);

            //Check if pagination is needed
            if (products.Count <= Constants.N_ITEMS_CARROUSSEL)
                context.Done<object>(null);
            else
            {
                reply = context.MakeMessage();
                reply.Attachments.Add(Common.PaginationCardAttachment());
                await context.PostAsync(reply);
                

                context.Wait(this.PaginationHandler);
            }
        }

        public async Task PaginationHandler(IDialogContext context, IAwaitable<IMessageActivity> result)
        {
            var activity = await result as Activity;

            if (activity.Text != null)
            {
                if (activity.Text.Equals(BotDefaultAnswers.next_pagination))
                    await ShowRecommendations(context, null);
                else
                    context.Done<object>(null);
            }
            else
                context.Done<object>(null);
        }

    }
}