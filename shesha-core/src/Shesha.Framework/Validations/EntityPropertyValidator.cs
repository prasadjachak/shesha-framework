﻿using Abp.Dependency;
using Abp.Domain.Entities;
using Shesha.Configuration.Runtime;
using Shesha.DynamicEntities.Cache;
using Shesha.DynamicEntities.Dtos;
using Shesha.Extensions;
using Shesha.Metadata;
using Shesha.Utilities;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Shesha.Validations
{
    public abstract class EntityPropertyValidator<TEntity, TId> : IPropertyValidator where TEntity : class, IEntity<TId>
    {
        public async Task<bool> ValidateObject(object obj, List<ValidationResult> validationResult, List<string> propertiesToValidate = null)
        {
            if (obj is TEntity entity)
            {
                return await ValidateEntity(entity, validationResult, propertiesToValidate);
            }
            return true;
        }
        public virtual async Task<bool> ValidateEntity(TEntity entity, List<ValidationResult> validationResult, List<string> propertiesToValidate = null)
        {
            return await Task.FromResult(true);
        }

        public async Task<bool> ValidateProperty(object obj, string propertyName, object value, List<ValidationResult> validationResult)
        {
            if (obj is TEntity entity)
            {
                return await ValidateEntityProperty(entity, propertyName, value, validationResult);
            }
            return true;
        }
        public virtual async Task<bool> ValidateEntityProperty(TEntity entity, string propertyName, object value, List<ValidationResult> validationResult)
        {
            return await Task.FromResult(true);
        }

    }

    public class EntityPropertyValidator : IPropertyValidator, ITransientDependency
    {
        private IEntityConfigCache _entityConfigCache;
        private IEntityConfigurationStore _entityConfigurationStore;

        public EntityPropertyValidator(IEntityConfigCache entityConfigCache, IEntityConfigurationStore entityConfigurationStore)
        {
            _entityConfigCache = entityConfigCache;
            _entityConfigurationStore = entityConfigurationStore;
        }

        public async Task<bool> ValidateProperty(object obj, string propertyName, object value, List<ValidationResult> validationResult)
        {
            if (!obj.GetType().IsEntityType() 
                && !obj.GetType().IsJsonEntityType())
                return true;

            var props = await _entityConfigCache.GetEntityPropertiesAsync(obj.GetType());

            if (props == null || !props.Any())
                return true;

            return Validate(obj, propertyName, value, validationResult, props, true);
        }

        public Task<bool> ValidateObject(object obj, List<ValidationResult> validationResult, List<string> propertiesToValidate = null)
        {
            return Task.FromResult(true);

            #region Validate all properties. Not needed if use ValidateObject
            /* if (!EntityHelper.IsEntity(obj.GetType()))
                return true;

            var props = await _entityConfigCache.GetEntityPropertiesAsync(obj.GetType());
            var config = await _entityConfigCache.GetEntityConfigAsync(obj.GetType());

            var pList = new List<string>();

            if (propertiesToValidate == null || !propertiesToValidate.Any())
            {
                Action<List<EntityPropertyDto>, string> propAdd = null;
                propAdd = (List<EntityPropertyDto> props, string root) =>
                {
                    foreach (var property in props.Where(x => !x.Suppress))
                    {
                        pList.Add(root + property.Name);
                        propAdd(property.Properties, root + property.Name + ".");
                    }
                };
                propAdd(props, "");
            }
            else
            {
                pList.AddRange(propertiesToValidate);
            }

            var vr = new List<ValidationResult>();
            foreach (var prop in pList.OrderBy(x => x))
            {
                Validate(obj, prop, null, vr, props, false);
            }

            validationResult.AddRange(vr);
            return !vr.Any();*/
            #endregion
        }

        public bool Validate(object obj, string propertyName, object value, List<ValidationResult> validationResult,
            List<EntityPropertyDto> props, bool useNewValue)
        {
            var parts = propertyName.Split('.').Select(x => x.ToCamelCase()).ToArray();

            var propConfig = props.FirstOrDefault(x => x.Name.ToCamelCase() == parts[0]);
            var propInfo = obj.GetType().GetProperties().FirstOrDefault(x => x.Name.ToCamelCase() == parts[0]);
            var innerObj = propInfo.GetValue(obj, null);

            var friendlyNameList = new List<string>() { propConfig.Label };

            var i = 1;
            while (i < parts.Length && propInfo != null && propConfig != null)
            {
                propConfig = propConfig.Properties.FirstOrDefault(x => x.Name.ToCamelCase() == parts[i]);
                propInfo = innerObj?.GetType().GetProperties().FirstOrDefault(x => x.Name.ToCamelCase() == parts[i]);
                innerObj = propInfo?.GetValue(innerObj, null);
                friendlyNameList.Add(propConfig.Label);
                i++;
            }

            var friendlyName = string.Join(".", friendlyNameList.Where(x => !string.IsNullOrWhiteSpace(x)));
            friendlyName = string.IsNullOrWhiteSpace(friendlyName) ? propertyName : friendlyName;

            if (propConfig == null)
                // ToDo: AS - may be need to create validation error
                return true;

            var prevValue = innerObj;

            if (!useNewValue) value = prevValue;

            var hasMessage = !string.IsNullOrWhiteSpace(propConfig.ValidationMessage);

            if (value == null && propConfig.Required && !propConfig.Suppress)
            {
                validationResult.Add(new ValidationResult(hasMessage
                    ? propConfig.ValidationMessage
                    : $"Property '{friendlyName}' is required."));
                return false;
            }

            if (useNewValue && prevValue == value)
                return true;

            if (useNewValue && propConfig.Suppress)
            {
                validationResult.Add(new ValidationResult($"Property '{friendlyName}' is suppressed."));
                return false;
            }

            if (useNewValue && propConfig.ReadOnly)
            {
                validationResult.Add(new ValidationResult($"Property '{friendlyName}' is readonly."));
                return false;
            }

            switch (propConfig.DataType)
            {
                case DataTypes.String:
                    if (propConfig.MinLength.HasValue && value.ToString().Length < propConfig.MinLength)
                    {
                        validationResult.Add(new ValidationResult(hasMessage
                            ? propConfig.ValidationMessage
                            : $"Property '{friendlyName}' should have value length more then {propConfig.MinLength - 1} symbols"));
                        return false;
                    }
                    if (propConfig.MaxLength.HasValue && value.ToString().Length > propConfig.MaxLength)
                    {
                        validationResult.Add(new ValidationResult(hasMessage
                            ? propConfig.ValidationMessage
                            : $"Property '{friendlyName}' should have value length less then {propConfig.MaxLength + 1} symbols"));
                        return false;
                    }
                    if (!string.IsNullOrWhiteSpace(propConfig.RegExp) && !(new Regex(propConfig.RegExp)).IsMatch(value.ToString()))
                    {
                        validationResult.Add(new ValidationResult(hasMessage
                            ? propConfig.ValidationMessage
                            : $"Property '{friendlyName}' should have value matched to `{propConfig.RegExp}` regular expression"));
                        return false;
                    }
                    break;
                case DataTypes.Number:
                    var b = double.TryParse(value?.ToString(), out double val);

                    if (!b)
                    {
                        validationResult.Add(new ValidationResult($"Property '{friendlyName}' should be in a number format"));
                        return false;
                    }

                    if (propConfig.Min.HasValue && val < propConfig.Min)
                    {
                        validationResult.Add(new ValidationResult(hasMessage
                            ? propConfig.ValidationMessage
                            : $"Property '{friendlyName}' should have value more or equal then {propConfig.Min}"));
                        return false;
                    }
                    if (propConfig.Max.HasValue && val > propConfig.Max)
                    {
                        validationResult.Add(new ValidationResult(hasMessage
                            ? propConfig.ValidationMessage
                            : $"Property '{friendlyName}' should have value less or equal then {propConfig.Max}"));
                        return false;
                    }
                    break;
            }

            return true;
        }
    }
}
